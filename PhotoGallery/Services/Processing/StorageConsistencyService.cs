using PhotoGallery.Data.Repositories;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Storage;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Reconciles storage objects against ProcessingQueueItem rows so a photo's DB state
/// always matches what is actually present in the storage provider.
///
/// Reference: D007 (Storage/Database Consistency Reconciliation).
///
/// This service is the counterpart to <see cref="PhotoConsistencyChecker"/>: that
/// class validates queue records against each other; this one validates queue records
/// against storage objects. They answer different questions and live side-by-side.
///
/// Lifetime: scoped (created per cycle by <c>StorageConsistencyWorker</c> and per
/// request by the admin reconcile endpoint). The internal <see cref="SemaphoreSlim"/>
/// is therefore per-instance, which is sufficient because both call sites resolve a
/// fresh scope every invocation and the application currently runs as a single
/// process — see D007 "Concurrency note" for the multi-instance caveat.
/// </summary>
public class StorageConsistencyService
{
    /// <summary>
    /// The four storage-relevant qualities. Note: D005 documents storage paths as
    /// {original/high/medium/low/raw}.jpg, but the actual <see cref="QualityType"/>
    /// enum is Thumbnail/Low/Medium/High (no raw). This is a known pre-existing
    /// doc/code drift in D005 — D007 follows the enum (which is what
    /// ImageProcessingService and PhotoVersionUrlService both use today).
    /// </summary>
    private static readonly QualityType[] AllQualities =
    {
        QualityType.Thumbnail,
        QualityType.Low,
        QualityType.Medium,
        QualityType.High,
    };

    private readonly IPhotoRepository _photoRepository;
    private readonly IProcessingQueueRepository _queueRepository;
    private readonly IProcessingQueueItemRepository _itemRepository;
    private readonly IPhotoVersionUrlRepository _urlRepository;
    private readonly IStorageProvider _storageProvider;
    private readonly ILogger<StorageConsistencyService> _logger;

    private readonly SemaphoreSlim _runLock = new(1, 1);

    public StorageConsistencyService(
        IPhotoRepository photoRepository,
        IProcessingQueueRepository queueRepository,
        IProcessingQueueItemRepository itemRepository,
        IPhotoVersionUrlRepository urlRepository,
        IStorageProvider storageProvider,
        ILogger<StorageConsistencyService> logger)
    {
        _photoRepository = photoRepository;
        _queueRepository = queueRepository;
        _itemRepository = itemRepository;
        _urlRepository = urlRepository;
        _storageProvider = storageProvider;
        _logger = logger;
    }

    /// <summary>
    /// Run a full reconciliation cycle. Safe to call concurrently — the internal
    /// semaphore serializes invocations so the worker's hourly tick and an admin
    /// triggered run cannot interleave.
    /// </summary>
    public async Task<ConsistencyReport> RunOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _runLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var report = new ConsistencyReport();
            var mutations = new MutationFlags();

            var photos = await _photoRepository.GetAllAsync();
            foreach (var photo in photos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReconcilePhotoAsync(photo, report, mutations, cancellationToken);
                report.PhotosScanned++;
            }

            // Persist every mutation made during the cycle. Without this, the scoped
            // DbContext is disposed by the caller (worker or admin endpoint) and all
            // tracked Add/Update calls are silently abandoned. Conditional so a no-op
            // cycle does not issue unnecessary SaveChangesAsync calls.
            if (mutations.ItemsChanged)
            {
                await _itemRepository.SaveChangesAsync();
            }
            if (mutations.QueuesChanged)
            {
                await _queueRepository.SaveChangesAsync();
            }
            if (mutations.UrlsChanged)
            {
                await _urlRepository.SaveChangesAsync();
            }

            _logger.LogInformation(
                "StorageConsistencyService cycle complete: scanned={Scanned} pending={Pending} backFilled={BackFilled} requeued={Requeued} originalsMissing={OriginalsMissing} queuesCreated={QueuesCreated} urlsInvalidated={UrlsInvalidated} uploadingPromoted={UploadingPromoted} uploadingFailed={UploadingFailed}",
                report.PhotosScanned, report.ItemsCreatedPending, report.ItemsBackFilledComplete,
                report.ItemsRequeued, report.OriginalsMissing, report.QueuesCreated, report.UrlsInvalidated,
                report.UploadingPromotedToPending, report.UploadingMarkedFailed);

            return report;
        }
        finally
        {
            _runLock.Release();
        }
    }

    /// <summary>
    /// Per-cycle flags recording which repositories had mutations and therefore need
    /// SaveChangesAsync called. Keeps the save logic data-driven instead of inferring
    /// from report counters (which mix counts of distinct mutation types per repo).
    /// </summary>
    private sealed class MutationFlags
    {
        public bool ItemsChanged;
        public bool QueuesChanged;
        public bool UrlsChanged;
        public bool PhotoChanged;
    }

    private async Task ReconcilePhotoAsync(Photo photo, ConsistencyReport report, MutationFlags mutations, CancellationToken cancellationToken)
    {
        var prefix = BuildPrefix(photo);
        var presentKeys = (await _storageProvider.ListAsync(prefix)).ToHashSet(StringComparer.Ordinal);
        var originalPresent = presentKeys.Contains(BuildKey(photo, "original"));

        // Uploading-photo reconciliation. Two outcomes:
        //   - Blob landed but the SPA never called /upload-complete (user closed the tab, network blip, etc.)
        //     -> transition to Pending and let the per-quality logic below enqueue the 4 base items.
        //   - Blob didn't land within 1 hour of UploadDate -> mark Failed so the row stops cluttering the UI.
        //     The OrphanedBlobReaper handles the storage-side cleanup independently.
        if (photo.ProcessingStatus == PhotoProcessingStatus.Uploading)
        {
            if (originalPresent)
            {
                _logger.LogInformation(
                    "Photo {PhotoId} stuck in Uploading but original.jpg is present in storage — promoting to Pending",
                    photo.Id);
                photo.ProcessingStatus = PhotoProcessingStatus.Pending;
                await _photoRepository.UpdateAsync(photo);
                await _photoRepository.SaveChangesAsync();
                report.UploadingPromotedToPending++;
                // Fall through — the per-quality reconciliation below will enqueue the 4 base items.
            }
            else
            {
                var staleAfter = TimeSpan.FromHours(1);
                if (DateTime.UtcNow - photo.UploadDate > staleAfter)
                {
                    _logger.LogWarning(
                        "Photo {PhotoId} stuck in Uploading for {Elapsed} with no blob — marking Failed",
                        photo.Id, DateTime.UtcNow - photo.UploadDate);
                    photo.ProcessingStatus = PhotoProcessingStatus.Failed;
                    await _photoRepository.UpdateAsync(photo);
                    await _photoRepository.SaveChangesAsync();
                    report.UploadingMarkedFailed++;
                }
                // Within the grace window: probably an upload still in progress. Leave alone.
                return;
            }
        }

        // Original-missing short-circuit: regeneration would have nothing to read from,
        // so do not touch any queue/item/url state. Just log and count.
        // Original-missing handling. Two outcomes depending on age:
        //   * Photo uploaded > 1h ago: the original is genuinely gone (chaos,
        //     manual storage cleanup, or partial-restore). Mark Failed so the
        //     UI stops showing it as Pending forever. Worker cannot re-derive
        //     thumbnails without a source. Cleanup of the dead Photo row is a
        //     separate decision the admin can make on the Photos table.
        //   * Photo within the last hour: probably still mid-upload via the
        //     Phase 2 direct-to-blob flow. Leave alone.
        if (!originalPresent)
        {
            var age = DateTime.UtcNow - photo.UploadDate;
            if (age > TimeSpan.FromHours(1)
                && photo.ProcessingStatus != PhotoProcessingStatus.Failed
                && photo.ProcessingStatus != PhotoProcessingStatus.Uploading)
            {
                _logger.LogWarning(
                    "Photo {PhotoId} (album {AlbumId}) missing original.jpg and uploaded {Age} ago — marking Failed",
                    photo.Id, photo.AlbumId, age);
                photo.ProcessingStatus = PhotoProcessingStatus.Failed;
                photo.ProcessingComplete = false;
                await _photoRepository.UpdateAsync(photo);
                await _photoRepository.SaveChangesAsync();
                mutations.PhotoChanged = true;
            }
            else
            {
                _logger.LogWarning(
                    "Photo {PhotoId} (album {AlbumId}) missing original.jpg in storage — skipping per-quality reconciliation (age={Age})",
                    photo.Id, photo.AlbumId, age);
            }
            report.OriginalsMissing++;
            return;
        }

        var queue = await _queueRepository.GetByPhotoIdAsync(photo.Id);
        if (queue == null)
        {
            queue = new ProcessingQueue
            {
                Id = Guid.NewGuid(),
                PhotoId = photo.Id,
                Status = ProcessingStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            };
            await _queueRepository.AddAsync(queue);
            mutations.QueuesChanged = true;
            report.QueuesCreated++;
            _logger.LogInformation(
                "Created missing ProcessingQueue {QueueId} for photo {PhotoId} during consistency reconciliation",
                queue.Id, photo.Id);
        }

        var existingItems = (await _itemRepository.GetByPhotoIdAsync(photo.Id)).ToList();

        foreach (var quality in AllQualities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = BuildKey(photo, quality.ToString().ToLowerInvariant());
            var present = presentKeys.Contains(key);
            var item = existingItems.FirstOrDefault(i => i.Quality == quality);
            await ReconcileQualityAsync(photo, queue, quality, present, item, report, mutations);
        }

        // Final pass: advance Photo.ProcessingStatus + Has* flags to match the
        // reality on disk. No active code path sets ProcessingStatus=Complete
        // anywhere, so without this step photos stay in Pending forever even
        // when their blobs all land. A photo is Complete when all 7 expected
        // blobs are on disk: original + thumbnail/low/medium/high +
        // thumbnail-watermarked + medium-watermarked.
        await ReconcilePhotoStatusAsync(photo, presentKeys, mutations);
    }

    /// <summary>
    /// Sync <see cref="Photo.ProcessingStatus"/> + per-quality Has* flags to
    /// what is actually present in storage. A photo is Complete iff ALL 7 of
    /// these blobs exist: original.jpg, thumbnail.jpg, low.jpg, medium.jpg,
    /// high.jpg, thumbnail-watermarked.jpg, medium-watermarked.jpg.
    /// The QualityType.Watermark queue item produces the two watermarked
    /// variants, so missing either means the watermark step has not run yet.
    /// </summary>
    private async Task ReconcilePhotoStatusAsync(
        Photo photo, HashSet<string> presentKeys, MutationFlags mutations)
    {
        bool hasOriginal = presentKeys.Contains(BuildKey(photo, "original"));
        bool hasThumb    = presentKeys.Contains(BuildKey(photo, "thumbnail"));
        bool hasLow      = presentKeys.Contains(BuildKey(photo, "low"));
        bool hasMedium   = presentKeys.Contains(BuildKey(photo, "medium"));
        bool hasHigh     = presentKeys.Contains(BuildKey(photo, "high"));
        bool hasThumbWm  = presentKeys.Contains(BuildKey(photo, "thumbnail-watermarked"));
        bool hasMediumWm = presentKeys.Contains(BuildKey(photo, "medium-watermarked"));
        bool allRequired = hasOriginal && hasThumb && hasLow && hasMedium
                           && hasHigh && hasThumbWm && hasMediumWm;

        var changed = false;
        if (photo.HasThumbnail != hasThumb)  { photo.HasThumbnail = hasThumb;  changed = true; }
        if (photo.HasLow       != hasLow)    { photo.HasLow       = hasLow;    changed = true; }
        if (photo.HasMedium    != hasMedium) { photo.HasMedium    = hasMedium; changed = true; }
        if (photo.HasHigh      != hasHigh)   { photo.HasHigh      = hasHigh;   changed = true; }

        var desired = allRequired ? PhotoProcessingStatus.Complete : PhotoProcessingStatus.Pending;
        // Do not touch Uploading or Failed states; earlier code in the same
        // method owns those transitions.
        if (photo.ProcessingStatus != PhotoProcessingStatus.Uploading
            && photo.ProcessingStatus != PhotoProcessingStatus.Failed
            && photo.ProcessingStatus != desired)
        {
            _logger.LogInformation(
                "Photo {PhotoId}: advancing ProcessingStatus {From} -> {To} (orig={O} T={T} L={L} M={M} H={H} Twm={TW} Mwm={MW})",
                photo.Id, photo.ProcessingStatus, desired,
                hasOriginal, hasThumb, hasLow, hasMedium, hasHigh, hasThumbWm, hasMediumWm);
            photo.ProcessingStatus = desired;
            if (desired == PhotoProcessingStatus.Complete)
            {
                photo.ProcessingComplete = true;
                photo.ProcessingCompletedAt = DateTime.UtcNow;
            }
            else
            {
                photo.ProcessingComplete = false;
                photo.ProcessingCompletedAt = null;
            }
            changed = true;
        }

        if (changed)
        {
            await _photoRepository.UpdateAsync(photo);
            await _photoRepository.SaveChangesAsync();
            mutations.PhotoChanged = true;
        }
    }

    private async Task ReconcileQualityAsync(
        Photo photo,
        ProcessingQueue queue,
        QualityType quality,
        bool present,
        ProcessingQueueItem? item,
        ConsistencyReport report,
        MutationFlags mutations)
    {
        if (item == null)
        {
            // CASE 1 / 3: no item record yet.
            if (present)
            {
                // Back-fill a Complete record so the DB reflects what's already in storage.
                var complete = new ProcessingQueueItem
                {
                    Id = Guid.NewGuid(),
                    PhotoId = photo.Id,
                    ProcessingQueueId = queue.Id,
                    Quality = quality,
                    Status = ProcessingStatus.Complete,
                    CompletedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                await _itemRepository.AddAsync(complete);
                mutations.ItemsChanged = true;
                report.ItemsBackFilledComplete++;
            }
            else
            {
                // Insert a Pending item so PhotoProcessingWorker will regenerate this quality.
                var pending = new ProcessingQueueItem
                {
                    Id = Guid.NewGuid(),
                    PhotoId = photo.Id,
                    ProcessingQueueId = queue.Id,
                    Quality = quality,
                    Status = ProcessingStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                await _itemRepository.AddAsync(pending);
                mutations.ItemsChanged = true;
                report.ItemsCreatedPending++;
            }

            return;
        }

        // Items in Processing: in-flight items are protected by an active lease.
        // A row is orphaned when either:
        //   - LeaseExpiresAt is in the past (the worker crashed mid-tick), OR
        //   - LeaseExpiresAt is NULL (a worker hit OperationCanceledException and
        //     called ReleaseLeaseAsync, which nulls the lease but doesn't reset
        //     the status — 41 stuck items in trial traced to this path).
        // Either way we reset to Pending so the next worker tick re-leases.
        if (item.Status == ProcessingStatus.Processing)
        {
            var leaseExpired = item.LeaseExpiresAt.HasValue && item.LeaseExpiresAt.Value < DateTime.UtcNow;
            var leaseNull    = !item.LeaseExpiresAt.HasValue;
            if (leaseExpired || leaseNull)
            {
                _logger.LogWarning(
                    "Orphaned Processing item {ItemId} (photo {PhotoId} quality {Quality}) — lease={LeaseState}; resetting to Pending",
                    item.Id, photo.Id, quality,
                    leaseNull ? "null" : item.LeaseExpiresAt!.Value.ToString("o"));
                ResetItemToPending(item);
                await _itemRepository.UpdateAsync(item);
                mutations.ItemsChanged = true;
                report.ItemsRequeued++;
            }
            // Active lease (worker still working): leave it alone.
            return;
        }

        // Phase 4 §1: there is no max-retry dead-letter any more. Every Error item is
        // a retry candidate forever (subject to the bounded-exponential backoff curve).
        // Falls through to the present/missing branches below.

        if (present)
        {
            // CASE 4 (Complete) → no-op. Error+CanRetry with storage present → mark Complete.
            if (item.Status == ProcessingStatus.Complete)
            {
                return;
            }

            // Pending or retryable Error: storage already has the file, so the work is effectively done.
            MarkItemComplete(item);
            await _itemRepository.UpdateAsync(item);
            mutations.ItemsChanged = true;
            report.ItemsBackFilledComplete++;
            return;
        }

        // CASE 2: storage missing. For Complete or retryable Error, flip back to Pending.
        if (item.Status == ProcessingStatus.Complete || item.Status == ProcessingStatus.Error)
        {
            var wasComplete = item.Status == ProcessingStatus.Complete;

            ResetItemToPending(item);
            await _itemRepository.UpdateAsync(item);
            mutations.ItemsChanged = true;

            if (wasComplete)
            {
                report.ItemsRequeued++;

                // Reopen the parent queue so the global status reflects pending work.
                if (queue.Status == ProcessingStatus.Complete)
                {
                    queue.Status = ProcessingStatus.Pending;
                    queue.CompletedAt = null;
                    queue.ErrorMessage = null;
                    await _queueRepository.UpdateAsync(queue);
                    mutations.QueuesChanged = true;
                }

                // Invalidate any cached pre-signed URL so the album-list endpoint stops
                // returning the stale URL. D008's CachePhotoVersionUrlAsync will overwrite
                // the inactive row in place when the URL is regenerated, so the unique
                // (PhotoId, Quality) index is never violated.
                var cachedUrl = await _urlRepository.GetByPhotoAndQualityAsync(photo.Id, quality);
                if (cachedUrl != null && cachedUrl.IsActive)
                {
                    cachedUrl.IsActive = false;
                    await _urlRepository.UpdateAsync(cachedUrl);
                    mutations.UrlsChanged = true;
                    report.UrlsInvalidated++;
                }
            }

            return;
        }

        // Pending + missing: nothing to do — PhotoProcessingWorker will pick it up on its next tick.
    }

    /// <summary>
    /// Album-scoped variant of <see cref="RunOnceAsync"/>. Reconciles only the
    /// photos in one album so the admin / album-owner can trigger a quick
    /// catch-up after spotting drift (e.g. an album says "still processing"
    /// after workers stopped, or photos render with a broken thumbnail).
    /// Same per-photo logic, just a narrower scan window.
    /// </summary>
    public async Task<ConsistencyReport> RunForAlbumAsync(Guid albumId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _runLock.WaitAsync(cancellationToken);
        try
        {
            var report = new ConsistencyReport();
            var mutations = new MutationFlags();

            // Pull only this album's photos. Includes Uploading rows
            // intentionally — Uploading photos missing their original.jpg
            // get logged as OriginalsMissing rather than triggering a
            // pointless requeue.
            var photos = await _photoRepository.GetAlbumPhotosIncludingUploadingAsync(albumId);
            foreach (var photo in photos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReconcilePhotoAsync(photo, report, mutations, cancellationToken);
                report.PhotosScanned++;
            }

            if (mutations.ItemsChanged)   await _itemRepository.SaveChangesAsync();
            if (mutations.QueuesChanged)  await _queueRepository.SaveChangesAsync();
            if (mutations.UrlsChanged)    await _urlRepository.SaveChangesAsync();

            _logger.LogInformation(
                "StorageConsistencyService album-scoped cycle for {AlbumId}: scanned={Scanned} pending={Pending} backFilled={BackFilled} requeued={Requeued} originalsMissing={OriginalsMissing} queuesCreated={QueuesCreated} urlsInvalidated={UrlsInvalidated} uploadingPromoted={UploadingPromoted} uploadingFailed={UploadingFailed}",
                albumId, report.PhotosScanned, report.ItemsCreatedPending, report.ItemsBackFilledComplete,
                report.ItemsRequeued, report.OriginalsMissing, report.QueuesCreated, report.UrlsInvalidated,
                report.UploadingPromotedToPending, report.UploadingMarkedFailed);

            return report;
        }
        finally
        {
            _runLock.Release();
        }
    }

    private static string BuildPrefix(Photo photo) => $"photogallery/{photo.AlbumId}/{photo.Id}/";

    private static string BuildKey(Photo photo, string qualityLowercase) => $"{BuildPrefix(photo)}{qualityLowercase}.jpg";

    /// <summary>
    /// Reset a queue item back to <see cref="ProcessingStatus.Pending"/> so
    /// <see cref="PhotoProcessingWorker"/> will pick it up on its next tick. Clears all
    /// retry metadata and the previous completion timestamp; updates <see cref="ProcessingQueueItem.UpdatedAt"/>.
    /// </summary>
    private static void ResetItemToPending(ProcessingQueueItem item)
    {
        item.Status = ProcessingStatus.Pending;
        item.CompletedAt = null;
        item.RetryCount = 0;
        item.LastError = null;
        item.NextRetryTime = null;
        item.Attempts = 0;
        item.LeaseExpiresAt = null;
        item.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark a queue item as <see cref="ProcessingStatus.Complete"/> with a current timestamp.
    /// Used for the present+Pending and present+retryable-Error reconciliation paths where
    /// storage already has the rendered file, so the DB just needs to catch up.
    /// </summary>
    private static void MarkItemComplete(ProcessingQueueItem item)
    {
        item.Status = ProcessingStatus.Complete;
        item.CompletedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;
        item.LastError = null;
    }
}

/// <summary>
/// Aggregate per-cycle counters returned from <see cref="StorageConsistencyService.RunOnceAsync"/>.
/// </summary>
public class ConsistencyReport
{
    public int PhotosScanned { get; set; }
    public int ItemsCreatedPending { get; set; }
    public int ItemsBackFilledComplete { get; set; }
    public int ItemsRequeued { get; set; }
    public int OriginalsMissing { get; set; }
    public int QueuesCreated { get; set; }
    public int UrlsInvalidated { get; set; }
    /// <summary>Photos that were stuck in Uploading status but had their original.jpg in storage — promoted to Pending.</summary>
    public int UploadingPromotedToPending { get; set; }
    /// <summary>Photos stuck in Uploading for over an hour with no blob — marked Failed so the UI stops showing them.</summary>
    public int UploadingMarkedFailed { get; set; }
}

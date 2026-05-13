using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Storage;
using System.Diagnostics;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Reaps orphaned blobs from the configured storage provider: blobs whose
/// containing <c>photogallery/&lt;albumGuid&gt;/</c> or
/// <c>photogallery/&lt;albumGuid&gt;/&lt;photoGuid&gt;/</c> prefix has no
/// matching DB row. This is the inverse direction of
/// <see cref="StorageConsistencyService"/> (which reconciles storage against
/// DB-tracked queue items): this service reconciles storage against the
/// authoritative Albums + Photos tables.
///
/// Reference: PhotoGallery v2 plan, Phase 5.
///
/// <para><b>Algorithm</b> — two-level hierarchy walk so we don't enumerate
/// millions of variant blobs:</para>
/// <list type="number">
///   <item>List immediate sub-prefixes under <c>photogallery/</c> (album GUIDs).</item>
///   <item>For each album GUID not in the Albums table, delete every blob
///         under that prefix.</item>
///   <item>For each album GUID that IS in the table, list its sub-prefixes
///         (photo GUIDs) and delete the ones whose Photo row is missing.</item>
/// </list>
///
/// <para><b>Grace window</b>: any blob whose <c>LastModified</c> is younger
/// than <see cref="_graceMinutes"/> is skipped to protect in-flight direct
/// uploads (Phase 2 introduces a flow where the blob exists before the DB
/// row). If a prefix contains any in-grace blob, the whole prefix is skipped
/// rather than partially deleted — a partial delete would leave a half-photo
/// in storage and let the soon-to-arrive DB row reference missing variants.</para>
///
/// <para><b>Idempotency / multi-replica safety</b>: <c>DeleteIfExists</c>
/// semantics make double-delete races harmless. Multiple replicas running
/// the reaper concurrently waste some scan work but cannot corrupt state.
/// The per-instance <see cref="SemaphoreSlim"/> serializes the worker tick
/// with admin-triggered runs within a single process.</para>
/// </summary>
public sealed class OrphanedBlobReaperService
{
    private const string ContainerRootPrefix = "photogallery/";

    private readonly IAlbumRepository _albumRepository;
    private readonly IPhotoRepository _photoRepository;
    private readonly IStorageProvider _storageProvider;
    private readonly ILogger<OrphanedBlobReaperService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly int _graceMinutes;

    private readonly SemaphoreSlim _runLock = new(1, 1);

    public OrphanedBlobReaperService(
        IAlbumRepository albumRepository,
        IPhotoRepository photoRepository,
        IStorageProvider storageProvider,
        IConfiguration configuration,
        ILogger<OrphanedBlobReaperService> logger,
        TimeProvider? timeProvider = null)
    {
        _albumRepository = albumRepository;
        _photoRepository = photoRepository;
        _storageProvider = storageProvider;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        var grace = configuration.GetValue("Storage:OrphanReapGraceMinutes", 60);
        _graceMinutes = grace < 0 ? 0 : grace;
    }

    /// <summary>
    /// Run a single reap pass. Returns a summary report. Safe to call
    /// concurrently within a process: the internal semaphore serializes
    /// invocations.
    /// </summary>
    public async Task<OrphanReapReport> RunOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _runLock.WaitAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var report = new OrphanReapReport();
            var cutoff = _timeProvider.GetUtcNow().AddMinutes(-_graceMinutes);

            _logger.LogInformation(
                "OrphanedBlobReaper run starting (grace={GraceMinutes}m, cutoff={Cutoff:o})",
                _graceMinutes, cutoff);

            var albumPrefixes = (await _storageProvider.ListSubPrefixesAsync(ContainerRootPrefix)).ToList();

            foreach (var albumPrefix in albumPrefixes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                report.AlbumsScanned++;

                if (!TryParseGuidFromPrefix(albumPrefix, ContainerRootPrefix, out var albumGuid))
                {
                    _logger.LogWarning(
                        "OrphanedBlobReaper: skipping non-GUID top-level prefix {Prefix}",
                        albumPrefix);
                    continue;
                }

                var album = await _albumRepository.GetByIdAsync(albumGuid);
                if (album is null)
                {
                    await ReapPrefixAsync(albumPrefix, cutoff, report, isAlbum: true, albumGuid, photoGuid: null, cancellationToken);
                    continue;
                }

                // Album exists. Walk one level deeper for photo GUIDs.
                var photoPrefixes = (await _storageProvider.ListSubPrefixesAsync(albumPrefix)).ToList();
                foreach (var photoPrefix in photoPrefixes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    report.PhotosScanned++;

                    if (!TryParseGuidFromPrefix(photoPrefix, albumPrefix, out var photoGuid))
                    {
                        _logger.LogWarning(
                            "OrphanedBlobReaper: skipping non-GUID photo prefix {Prefix}",
                            photoPrefix);
                        continue;
                    }

                    var photo = await _photoRepository.GetByIdAsync(photoGuid);
                    if (photo is null || photo.AlbumId != albumGuid)
                    {
                        await ReapPrefixAsync(photoPrefix, cutoff, report, isAlbum: false, albumGuid, photoGuid, cancellationToken);
                    }
                }
            }

            stopwatch.Stop();
            report.ElapsedMs = stopwatch.ElapsedMilliseconds;

            _logger.LogInformation(
                "OrphanedBlobReaper run complete: albumsScanned={AlbumsScanned} photosScanned={PhotosScanned} blobsScanned={BlobsScanned} orphanedAlbums={OrphanedAlbums} orphanedPhotos={OrphanedPhotos} blobsDeleted={BlobsDeleted} bytesReclaimed={BytesReclaimed} skippedByGrace={SkippedByGrace} elapsedMs={ElapsedMs}",
                report.AlbumsScanned, report.PhotosScanned, report.BlobsScanned,
                report.OrphanedAlbums.Count, report.OrphanedPhotos.Count,
                report.BlobsDeleted, report.BytesReclaimed, report.SkippedByGracePeriod, report.ElapsedMs);

            return report;
        }
        finally
        {
            _runLock.Release();
        }
    }

    /// <summary>
    /// Enumerate every blob under <paramref name="prefix"/> with metadata,
    /// apply the grace-window filter, and (if no blob is in-grace) batch-delete
    /// them. If any blob is in-grace the whole prefix is skipped to avoid
    /// half-deleting an in-flight upload.
    /// </summary>
    private async Task ReapPrefixAsync(
        string prefix,
        DateTimeOffset cutoff,
        OrphanReapReport report,
        bool isAlbum,
        Guid albumGuid,
        Guid? photoGuid,
        CancellationToken cancellationToken)
    {
        var blobs = (await _storageProvider.ListWithMetadataAsync(prefix)).ToList();
        report.BlobsScanned += blobs.Count;

        var inGrace = blobs.Where(b => b.LastModified > cutoff).ToList();
        if (inGrace.Count > 0)
        {
            report.SkippedByGracePeriod += blobs.Count;
            _logger.LogInformation(
                "OrphanedBlobReaper: skipping prefix {Prefix} ({Count} blobs) — {InGrace} blob(s) within grace window; will retry next cycle",
                prefix, blobs.Count, inGrace.Count);
            return;
        }

        if (blobs.Count == 0)
        {
            // No blobs under prefix (or listing failed) — nothing to do.
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var keys = blobs.Select(b => b.Key).ToList();
        var totalBytes = blobs.Sum(b => b.Size);
        var deleted = await _storageProvider.DeleteManyAsync(keys);

        report.BlobsDeleted += deleted;
        report.BytesReclaimed += totalBytes;

        if (isAlbum)
        {
            report.OrphanedAlbums.Add(albumGuid);
            _logger.LogInformation(
                "Reaped orphaned album {AlbumGuid}: {Deleted}/{Total} blobs, {Bytes} bytes reclaimed",
                albumGuid, deleted, blobs.Count, totalBytes);
        }
        else
        {
            report.OrphanedPhotos.Add(photoGuid!.Value);
            _logger.LogInformation(
                "Reaped orphaned photo {PhotoGuid} (album {AlbumGuid}): {Deleted}/{Total} blobs, {Bytes} bytes reclaimed",
                photoGuid, albumGuid, deleted, blobs.Count, totalBytes);
        }
    }

    /// <summary>
    /// Parse the GUID segment from a hierarchical sub-prefix of the form
    /// <c>{parentPrefix}{guid}/</c>. Returns false if the segment isn't a GUID
    /// (defensive — we don't want to delete unexpected layout).
    /// </summary>
    internal static bool TryParseGuidFromPrefix(string subPrefix, string parentPrefix, out Guid guid)
    {
        guid = Guid.Empty;
        if (string.IsNullOrEmpty(subPrefix)) return false;
        if (!subPrefix.StartsWith(parentPrefix, StringComparison.Ordinal)) return false;

        var tail = subPrefix.Substring(parentPrefix.Length).TrimEnd('/');
        return Guid.TryParse(tail, out guid);
    }
}

/// <summary>
/// Summary report returned from <see cref="OrphanedBlobReaperService.RunOnceAsync"/>.
/// Matches the JSON contract shipped on <c>POST /api/photos/admin/reap-orphans</c>.
/// </summary>
public sealed class OrphanReapReport
{
    public ReapScanCounts Scanned { get; } = new();
    public List<Guid> OrphanedAlbums { get; } = new();
    public List<Guid> OrphanedPhotos { get; } = new();
    public int BlobsDeleted { get; set; }
    public long BytesReclaimed { get; set; }
    public int SkippedByGracePeriod { get; set; }
    public long ElapsedMs { get; set; }

    // Convenience setters that route into the nested Scanned record.
    internal int AlbumsScanned { get => Scanned.Albums; set => Scanned.Albums = value; }
    internal int PhotosScanned { get => Scanned.Photos; set => Scanned.Photos = value; }
    internal int BlobsScanned { get => Scanned.Blobs; set => Scanned.Blobs = value; }
}

/// <summary>Per-cycle scan counters (nested under <c>scanned</c> in the JSON response).</summary>
public sealed class ReapScanCounts
{
    public int Albums { get; set; }
    public int Photos { get; set; }
    public int Blobs { get; set; }
}

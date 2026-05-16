using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Microsoft.AspNetCore.SignalR;
using PhotoGallery.Hubs;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Storage;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Enums;
using System.Threading.Channels;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Service for processing photos with multiple compression levels.
/// Uses ProcessingQueue and ProcessingQueueItem for tracking per-quality processing.
/// Reference: D003 (Image Processing with Compression Profiles), Phase 4 (resilient
/// retries + in-instance parallelism + DB lease).
/// </summary>
public class ImageProcessingService : IImageProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IStorageProvider _storageProvider;
    private readonly IHubContext<PhotoProgressHub>? _progressHub;
    private readonly ILogger<ImageProcessingService> _logger;
    private readonly int _workerParallelism;
    private readonly int _leaseBatchMultiplier;
    private readonly TimeSpan _leaseDuration = TimeSpan.FromMinutes(5);
    private CancellationTokenSource? _processingCts;
    private Task? _processingTask;

    private static readonly Dictionary<QualityType, (int width, int height, int quality)> QualityDimensions = new()
    {
        { QualityType.Thumbnail, (200, 200, 60) },    // Thumbnail
        { QualityType.Low, (800, 800, 70) },          // Mobile/web
        { QualityType.Medium, (1920, 1920, 85) },     // Desktop/email
        { QualityType.High, (3840, 3840, 95) }        // Print/download
    };

    /// <summary>
    /// The 4 base resize qualities. The Watermark pseudo-quality is enqueued as a separate
    /// item once all four of these complete. Reference: Phase 4 §2.
    /// </summary>
    private static readonly QualityType[] BaseQualities =
    {
        QualityType.Thumbnail,
        QualityType.Low,
        QualityType.Medium,
        QualityType.High,
    };

    public ImageProcessingService(
        IServiceProvider serviceProvider,
        IStorageProvider storageProvider,
        ILogger<ImageProcessingService> logger,
        IConfiguration configuration,
        IHubContext<PhotoProgressHub>? progressHub = null)
    {
        _serviceProvider = serviceProvider;
        _storageProvider = storageProvider;
        _logger = logger;

        // Phase 4 §3: in-instance parallel consumers. Default 5; clamp to [1, 64] to
        // avoid an operator pasting nonsense into the appsetting and pinning a worker.
        var configured = configuration.GetValue<int>("PhotoProcessing:WorkerParallelism", 5);
        _workerParallelism = Math.Clamp(configured, 1, 64);
        var configuredMultiplier = configuration.GetValue<int>("PhotoProcessing:LeaseBatchMultiplier", 4);
        _leaseBatchMultiplier = Math.Clamp(configuredMultiplier, 1, 32);
        _progressHub = progressHub;
    }

    /// <summary>Queue a photo for processing with all 4 quality versions</summary>
    public async Task<string> QueuePhotoAsync(Guid photoId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var queueRepository = scope.ServiceProvider.GetRequiredService<IProcessingQueueRepository>();
            var itemRepository = scope.ServiceProvider.GetRequiredService<IProcessingQueueItemRepository>();

            var queue = new ProcessingQueue { PhotoId = photoId };
            await queueRepository.AddAsync(queue);
            await queueRepository.SaveChangesAsync();

            // Create 4 processing items (one for each base quality). The Watermark item
            // is enqueued later by EnqueueWatermarkIfReadyAsync once all four complete.
            foreach (var quality in BaseQualities)
            {
                var item = new ProcessingQueueItem
                {
                    ProcessingQueueId = queue.Id,
                    PhotoId = photoId,
                    Quality = quality,
                    Status = ProcessingStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };
                await itemRepository.AddAsync(item);
            }
            await itemRepository.SaveChangesAsync();

            _logger.LogInformation("Queued photo {PhotoId} for processing with 4 quality versions", photoId);
            return queue.Id.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queuing photo {PhotoId}", photoId);
            throw;
        }
    }

    /// <summary>String version for API compatibility</summary>
    public async Task<string> QueuePhotoAsync(string photoId)
    {
        if (!Guid.TryParse(photoId, out var photoGuid))
            throw new ArgumentException("Invalid photo ID format", nameof(photoId));
        return await QueuePhotoAsync(photoGuid);
    }

    /// <summary>
    /// Best-effort broadcast to the uploader's hub group. The hub is optional
    /// (Phase 3) — if no <see cref="IHubContext{T}"/> was injected, or the
    /// send throws, the worker continues normally. Tests that don't wire
    /// SignalR can leave the constructor argument null.
    /// </summary>
    private async Task BroadcastAsync(string? userId, string eventName, object payload)
    {
        if (_progressHub == null || string.IsNullOrEmpty(userId))
            return;
        try
        {
            await _progressHub.Clients
                .Group(PhotoProgressHub.UserGroup(userId))
                .SendAsync(eventName, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to broadcast {EventName} to user group {UserId} — non-fatal",
                eventName, userId);
        }
    }

    /// <summary>
    /// Process all pending queue items. Phase 4 §3: lease a batch via the repo's
    /// DB-level lease, then drain through a bounded channel with
    /// <see cref="Parallel.ForEachAsync"/> at the configured parallelism. Each consumer
    /// gets its own DI scope so per-quality ImageSharp <c>Image</c> instances never
    /// cross threads. Reference: Phase 4 scope §3 + §4.
    /// </summary>
    public async Task ProcessQueueAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Read throttle settings live each tick so admin changes hot-reload
            // without a restart. Falls back to the construction-time defaults
            // when the RuntimeSettings table has no override.
            int liveParallelism = _workerParallelism;
            int liveMultiplier = _leaseBatchMultiplier;
            using (var settingsScope = _serviceProvider.CreateScope())
            {
                var resolver = settingsScope.ServiceProvider.GetRequiredService<ISettingsResolver>();
                liveParallelism = Math.Clamp(
                    await resolver.GetIntAsync("PhotoProcessing:WorkerParallelism", _workerParallelism, cancellationToken),
                    1, 64);
                liveMultiplier = Math.Clamp(
                    await resolver.GetIntAsync("PhotoProcessing:LeaseBatchMultiplier", _leaseBatchMultiplier, cancellationToken),
                    1, 32);
            }

            // Lease a batch of work — Pending OR (Error AND retry-due) AND lease-free.
            // The lease itself is set atomically by the repo so two workers (in-instance
            // or cross-instance once we scale ACA replicas) never claim the same row.
            IReadOnlyList<ProcessingQueueItem> leased;
            using (var leaseScope = _serviceProvider.CreateScope())
            {
                var itemRepository = leaseScope.ServiceProvider.GetRequiredService<IProcessingQueueItemRepository>();
                // Lease up to 4x parallelism so consumers always have queued work even
                // while some are mid-flight. 5 min lease > worst-case single-photo
                // processing time so a slow consumer doesn't have its row stolen.
                leased = await itemRepository.LeaseNextBatchAsync(
                    liveParallelism * liveMultiplier, _leaseDuration, cancellationToken);
            }

            if (leased.Count == 0)
                return;

            _logger.LogDebug("Leased {Count} items for processing (parallelism={Parallelism})",
                leased.Count, liveParallelism);

            // Funnel into a Channel so we have backpressure if a slow consumer falls
            // behind, and so cancellation cleanly drains in-flight items.
            var channel = Channel.CreateBounded<ProcessingQueueItem>(new BoundedChannelOptions(leased.Count)
            {
                SingleReader = false,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
            foreach (var item in leased)
                await channel.Writer.WriteAsync(item, cancellationToken);
            channel.Writer.Complete();

            var processedPhotos = new System.Collections.Concurrent.ConcurrentBag<Guid>();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = liveParallelism,
                CancellationToken = cancellationToken,
            };

            await Parallel.ForEachAsync(
                channel.Reader.ReadAllAsync(cancellationToken),
                parallelOptions,
                async (item, ct) =>
                {
                    // Per-consumer DI scope — each ImageSharp Image instance lives inside
                    // one scope's lifetime and never crosses threads.
                    using var scope = _serviceProvider.CreateScope();
                    var itemRepository = scope.ServiceProvider.GetRequiredService<IProcessingQueueItemRepository>();
                    var photoRepository = scope.ServiceProvider.GetRequiredService<IPhotoRepository>();
                    var watermarkTextCache = new Dictionary<string, string>(StringComparer.Ordinal);

                    try
                    {
                        await ProcessQualityAsync(item, scope.ServiceProvider, itemRepository, photoRepository, watermarkTextCache, ct);
                        await itemRepository.MarkCompleteAsync(item.Id);
                        _logger.LogInformation("Completed processing photo {PhotoId} quality {Quality}",
                            item.PhotoId, item.Quality);
                        processedPhotos.Add(item.PhotoId);
                    }
                    catch (OperationCanceledException)
                    {
                        // Release the lease so another worker can pick this up.
                        try { await itemRepository.ReleaseLeaseAsync(item.Id, CancellationToken.None); } catch { }
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing item {ItemId} (photo {PhotoId}, quality {Quality})",
                            item.Id, item.PhotoId, item.Quality);

                        // Increment retry counter + compute next backoff. MarkFailedAsync
                        // clears the lease so the row is eligible on the next pass.
                        var fresh = await itemRepository.GetByIdAsync(item.Id);
                        if (fresh != null)
                        {
                            fresh.IncrementRetry(ex.Message);
                            fresh.Status = ProcessingStatus.Error;
                            fresh.LastError = ex.Message;
                            fresh.LeaseExpiresAt = null;
                            fresh.UpdatedAt = DateTime.UtcNow;
                            await itemRepository.UpdateAsync(fresh);
                            await itemRepository.SaveChangesAsync();
                        }
                    }
                });

            // Watermark enqueue + URL generation pass. Single-threaded after the parallel
            // work drains so we touch each photo's "completion" state exactly once.
            foreach (var photoId in processedPhotos.Distinct())
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                using var scope = _serviceProvider.CreateScope();
                var itemRepository = scope.ServiceProvider.GetRequiredService<IProcessingQueueItemRepository>();
                var urlService = scope.ServiceProvider.GetRequiredService<PhotoVersionUrlService>();

                try
                {
                    var queueId = (await itemRepository.GetByPhotoIdAsync(photoId)).FirstOrDefault()?.ProcessingQueueId;
                    if (!queueId.HasValue)
                        continue;

                    var allItems = (await itemRepository.GetByQueueIdAsync(queueId.Value)).ToList();
                    var baseItems = allItems.Where(i => i.Quality != QualityType.Watermark).ToList();

                    var allBaseComplete = baseItems.Count == BaseQualities.Length
                        && baseItems.All(i => i.Status == ProcessingStatus.Complete);

                    if (allBaseComplete)
                    {
                        // Phase 4 §2: enqueue a single Watermark item once per photo so
                        // watermark rendering gets the same backoff/retry plumbing as the
                        // other qualities. Idempotent — skip if one already exists.
                        await EnqueueWatermarkIfMissingAsync(itemRepository, queueId.Value, photoId, allItems);

                        _logger.LogDebug("All base qualities complete for photo {PhotoId}. Generating pre-signed URLs...", photoId);
                        await urlService.GeneratePhotoVersionUrlsAsync(photoId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error finalizing photo {PhotoId}", photoId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the normal shutdown path — not an error.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProcessQueueAsync");
        }
    }

    /// <summary>
    /// Add a Watermark queue item for the photo if one does not already exist.
    /// Phase 4 §2 — promotes watermark generation to a first-class queue item so it
    /// gets the same backoff + indefinite retry treatment as the other qualities.
    /// </summary>
    internal static async Task EnqueueWatermarkIfMissingAsync(
        IProcessingQueueItemRepository itemRepository,
        Guid queueId,
        Guid photoId,
        IReadOnlyList<ProcessingQueueItem> existingItems)
    {
        if (existingItems.Any(i => i.Quality == QualityType.Watermark))
            return;

        var watermarkItem = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queueId,
            PhotoId = photoId,
            Quality = QualityType.Watermark,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await itemRepository.AddAsync(watermarkItem);
        await itemRepository.SaveChangesAsync();
    }

    /// <summary>Process a single quality version of a photo</summary>
    private async Task ProcessQualityAsync(
        ProcessingQueueItem item,
        IServiceProvider scopedProvider,
        IProcessingQueueItemRepository itemRepository,
        IPhotoRepository photoRepository,
        IDictionary<string, string> watermarkTextCache,
        CancellationToken cancellationToken)
    {
        // Mark as processing
        item.Status = ProcessingStatus.Processing;
        await itemRepository.UpdateAsync(item);
        await itemRepository.SaveChangesAsync();

        // Get photo to find album ID
        var photo = await photoRepository.GetByIdAsync(item.PhotoId);
        if (photo == null)
            throw new FileNotFoundException($"Photo not found: {item.PhotoId}");

        // Watermark pseudo-quality: render watermarked Thumbnail + Medium variants
        // from the already-rendered base files in storage. Phase 4 §2.
        if (item.Quality == QualityType.Watermark)
        {
            await ProcessWatermarkAsync(photo, item, scopedProvider, watermarkTextCache, cancellationToken);
            return;
        }

        // Phase 3 — per-quality ProcessingStarted broadcast to the uploader.
        await BroadcastAsync(photo.UploadedBy, PhotoProgressEvents.ProcessingStarted,
            new ProcessingStartedPayload(item.PhotoId.ToString(), item.Quality.ToString()));

        // Get original from storage
        var originalPath = $"photogallery/{photo.AlbumId}/{item.PhotoId}/original.jpg";
        if (!await _storageProvider.ExistsAsync(originalPath))
            throw new FileNotFoundException($"Original photo not found at {originalPath}");

        using (var originalStream = await _storageProvider.DownloadAsync(originalPath))
        {
            if (originalStream == null)
                throw new InvalidOperationException($"Cannot read original photo from {originalPath}");

            // 25%: original downloaded.
            await BroadcastAsync(photo.UploadedBy, PhotoProgressEvents.ProcessingProgress,
                new ProcessingProgressPayload(item.PhotoId.ToString(), item.Quality.ToString(), 25));

            // Load image
            using (var image = await Image.LoadAsync(originalStream, cancellationToken))
            {
                // Get dimensions and quality for this quality level
                var (width, height, quality) = QualityDimensions[item.Quality];

                // Resize image
                image.Mutate(x => x.Resize(
                    new ResizeOptions
                    {
                        Size = new Size(width, height),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3
                    }));

                // 50%: resize complete.
                await BroadcastAsync(photo.UploadedBy, PhotoProgressEvents.ProcessingProgress,
                    new ProcessingProgressPayload(item.PhotoId.ToString(), item.Quality.ToString(), 50));

                // Save to storage
                var qualityName = item.Quality switch
                {
                    QualityType.Thumbnail => "thumbnail",
                    QualityType.Low => "low",
                    QualityType.Medium => "medium",
                    QualityType.High => "high",
                    _ => "medium"
                };
                var outputPath = $"photogallery/{photo.AlbumId}/{item.PhotoId}/{qualityName}.jpg";
                using (var outputStream = new MemoryStream())
                {
                    await image.SaveAsJpegAsync(outputStream, new JpegEncoder { Quality = quality }, cancellationToken);
                    outputStream.Position = 0;
                    await _storageProvider.UploadAsync(outputPath, outputStream, "image/jpeg");
                }

                // 75%: variant uploaded.
                await BroadcastAsync(photo.UploadedBy, PhotoProgressEvents.ProcessingProgress,
                    new ProcessingProgressPayload(item.PhotoId.ToString(), item.Quality.ToString(), 75));

                _logger.LogInformation("Saved {Quality} version to {Path}", item.Quality, outputPath);

                // 100%: success. Completion broadcast (with blobPath) so the
                // SPA can flip the per-quality progress bar to "done" without
                // re-querying.
                await BroadcastAsync(photo.UploadedBy, PhotoProgressEvents.ProcessingCompleted,
                    new ProcessingCompletedPayload(
                        item.PhotoId.ToString(),
                        item.Quality.ToString(),
                        Success: true,
                        BlobPath: outputPath,
                        Error: null));
            }
        }
    }

    /// <summary>
    /// Render and store watermarked variants of the Thumbnail + Medium base files.
    /// Reads the (already-uploaded) base JPEGs from storage so this method has no
    /// dependency on the in-process <see cref="Image"/> object that produced them.
    /// Failures throw — the caller increments retry/backoff so a font-init / blob
    /// transient gets the same indefinite-retry treatment as the other qualities.
    /// Reference: Phase 4 §2 (watermark gets its own queue item) + D009.
    /// </summary>
    private async Task ProcessWatermarkAsync(
        Photo photo,
        ProcessingQueueItem item,
        IServiceProvider scopedProvider,
        IDictionary<string, string> watermarkTextCache,
        CancellationToken cancellationToken)
    {
        var watermarkService = scopedProvider.GetRequiredService<WatermarkService>();
        var watermarkText = await ResolveWatermarkTextAsync(
            scopedProvider, photo.UploadedBy, watermarkTextCache, cancellationToken);

        // Quality used for re-encoding the watermarked output. Mirrors the dimensions
        // table's quality column for each rendered base.
        var watermarkTargets = new (string baseName, int jpegQuality)[]
        {
            ("thumbnail", QualityDimensions[QualityType.Thumbnail].quality),
            ("medium",    QualityDimensions[QualityType.Medium].quality),
        };

        foreach (var (baseName, jpegQuality) in watermarkTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceKey = $"photogallery/{photo.AlbumId}/{item.PhotoId}/{baseName}.jpg";
            if (!await _storageProvider.ExistsAsync(sourceKey))
                throw new FileNotFoundException($"Base variant missing for watermark: {sourceKey}");

            using var sourceStream = await _storageProvider.DownloadAsync(sourceKey)
                ?? throw new InvalidOperationException($"Cannot read {sourceKey}");
            using var watermarkedStream = new MemoryStream();

            await watermarkService.ApplyWatermarkAsync(
                sourceStream, watermarkedStream, watermarkText, jpegQuality, cancellationToken);
            watermarkedStream.Position = 0;

            var outputKey = $"photogallery/{photo.AlbumId}/{item.PhotoId}/{baseName}-watermarked.jpg";
            await _storageProvider.UploadAsync(outputKey, watermarkedStream, "image/jpeg");

            _logger.LogInformation("Saved watermarked {BaseName} variant to {Path}", baseName, outputKey);
        }
    }

    /// <summary>
    /// Resolve "© {display name}" for an uploader, memoizing in the supplied per-batch cache.
    /// Falls back to "© Photo Gallery" on any resolver failure — watermark generation must
    /// never block on auth/identity lookups.
    /// </summary>
    private async Task<string> ResolveWatermarkTextAsync(
        IServiceProvider scopedProvider,
        string? uploadedBy,
        IDictionary<string, string> cache,
        CancellationToken ct)
    {
        var key = uploadedBy ?? string.Empty;
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        string text;
        try
        {
            var resolver = scopedProvider.GetRequiredService<IWatermarkTextResolver>();
            text = await resolver.ResolveAsync(uploadedBy, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Watermark display-name resolution failed for uploader {UploadedBy}; falling back to generic.",
                uploadedBy);
            text = "© Photo Gallery";
        }

        cache[key] = text;
        return text;
    }

    /// <summary>Get processing status for a queue</summary>
    public async Task<Dictionary<string, object>> GetQueueStatusAsync(Guid queueId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var queueRepository = scope.ServiceProvider.GetRequiredService<IProcessingQueueRepository>();
            var itemRepository = scope.ServiceProvider.GetRequiredService<IProcessingQueueItemRepository>();

            var queue = await queueRepository.GetByIdAsync(queueId);
            if (queue == null)
                return new Dictionary<string, object> { { "error", "Queue not found" } };

            var items = await itemRepository.GetByQueueIdAsync(queueId);

            var completedCount = items.Count(i => i.Status == ProcessingStatus.Complete);
            var totalCount = items.Count();
            var percentComplete = totalCount > 0 ? (completedCount * 100) / totalCount : 0;

            return new Dictionary<string, object>
            {
                { "queueId", queueId },
                { "photoId", queue.PhotoId },
                { "status", queue.Status.ToString() },
                { "completedCount", completedCount },
                { "totalCount", totalCount },
                { "percentComplete", percentComplete },
                { "items", items.Select(i => new
                {
                    i.Quality,
                    i.Status,
                    i.RetryCount,
                    i.LastError
                }).ToList() }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting queue status for {QueueId}", queueId);
            return new Dictionary<string, object> { { "error", ex.Message } };
        }
    }

    /// <summary>Retry a failed processing item</summary>
    public async Task RetryFailedItemAsync(Guid itemId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var itemRepository = scope.ServiceProvider.GetRequiredService<IProcessingQueueItemRepository>();

            var item = await itemRepository.GetByIdAsync(itemId);
            if (item == null)
                throw new ArgumentException($"Item {itemId} not found");

            if (item.Status != ProcessingStatus.Error)
                throw new InvalidOperationException($"Cannot retry item in {item.Status} status");

            item.Status = ProcessingStatus.Pending;
            item.LastError = null;
            await itemRepository.UpdateAsync(item);
            await itemRepository.SaveChangesAsync();

            _logger.LogInformation("Reset item {ItemId} for retry", itemId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrying item {ItemId}", itemId);
            throw;
        }
    }

    /// <summary>Get items ready for retry</summary>
    public async Task<IEnumerable<ProcessingQueueItem>> GetReadyForRetryAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var itemRepository = scope.ServiceProvider.GetRequiredService<IProcessingQueueItemRepository>();
        return await itemRepository.GetReadyForRetryAsync();
    }

    /// <summary>Start the background processing worker</summary>
    public async Task StartProcessingWorkerAsync(CancellationToken applicationStopping)
    {
        _processingCts = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
        _processingTask = ProcessingWorkerAsync(_processingCts.Token);

        _logger.LogInformation("Image processing worker started");
        await Task.CompletedTask;
    }

    /// <summary>Background worker that continuously processes the queue</summary>
    private async Task ProcessingWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessQueueAsync(cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in processing worker");
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
            }
        }
        finally
        {
            _logger.LogInformation("Image processing worker stopped");
        }
    }

    public IEnumerable<CompressionProfile> GetCompressionProfiles()
    {
        return new List<CompressionProfile>
        {
            new CompressionProfile { Name = "thumbnail", QualityPercentage = 60, Description = "Thumbnail (200x200, 60% quality)" },
            new CompressionProfile { Name = "low", QualityPercentage = 70, Description = "Low compression (800x800, 70% quality)" },
            new CompressionProfile { Name = "medium", QualityPercentage = 85, Description = "Medium compression (1920x1920, 85% quality)" },
            new CompressionProfile { Name = "high", QualityPercentage = 95, Description = "High quality (3840x3840, 95% quality)" }
        };
    }

    // Legacy PhotoVersion methods - kept for API compatibility but not used in new pipeline
    public async Task<IEnumerable<PhotoVersion>> GetPhotoVersionsAsync(string photoId)
    {
        return Enumerable.Empty<PhotoVersion>();
    }

    public async Task<PhotoVersion?> GetPhotoVersionAsync(string photoId, string quality)
    {
        try
        {
            // Parse the photo ID to get the album context
            if (!Guid.TryParse(photoId, out var photoGuid))
                return null;

            using var scope = _serviceProvider.CreateScope();
            var photoRepository = scope.ServiceProvider.GetRequiredService<IPhotoRepository>();
            
            // Get the photo to retrieve album ID
            var photo = await photoRepository.GetByIdAsync(photoGuid);
            if (photo == null)
                return null;

            // Map quality string to enum to get numeric value (for backwards compatibility)
            var qualityEnum = quality.ToLower() switch
            {
                "thumbnail" => QualityType.Thumbnail,
                "low" => QualityType.Low,
                "medium" => QualityType.Medium,
                "high" => QualityType.High,
                "original" => QualityType.Original,
                _ => QualityType.Medium
            };

            // Try the new string-based format first (thumbnail.jpg, low.jpg, etc.)
            var storageKey = $"photogallery/{photo.AlbumId}/{photoId}/{quality.ToLower()}.jpg";
            var exists = await _storageProvider.ExistsAsync(storageKey);

            // If not found, try the old numeric format (0.jpg, 1.jpg, etc.) for backwards compatibility
            if (!exists)
            {
                storageKey = $"photogallery/{photo.AlbumId}/{photoId}/{(int)qualityEnum}.jpg";
                exists = await _storageProvider.ExistsAsync(storageKey);
            }

            if (!exists)
            {
                _logger.LogWarning("Photo version file not found in storage for {PhotoId} quality {Quality}", photoId, quality);
                return null;
            }

            // Map quality string to PhotoQuality enum
            var photoQuality = quality.ToLower() switch
            {
                "thumbnail" => PhotoQuality.HighCompression,  // Thumbnail is most compressed
                "low" => PhotoQuality.HighCompression,
                "medium" => PhotoQuality.MediumCompression,
                "high" => PhotoQuality.LowCompression,
                _ => PhotoQuality.MediumCompression
            };

            // Return a PhotoVersion object for the controller to use
            return new PhotoVersion
            {
                Id = Guid.NewGuid(),
                PhotoId = photoGuid,
                Quality = photoQuality,
                StorageKey = storageKey,
                FileSize = 0,  // We don't know the size, will be updated by storage provider if needed
                ProcessedDate = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting photo version for {PhotoId} with quality {Quality}", photoId, quality);
            return null;
        }
    }
}

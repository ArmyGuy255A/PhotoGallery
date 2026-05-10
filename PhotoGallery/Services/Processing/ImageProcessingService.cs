using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Storage;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Enums;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Service for processing photos with multiple compression levels.
/// Uses ProcessingQueue and ProcessingQueueItem for tracking per-quality processing.
/// Reference: D003 (Image Processing with Compression Profiles)
/// </summary>
public class ImageProcessingService : IImageProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IStorageProvider _storageProvider;
    private readonly ILogger<ImageProcessingService> _logger;
    private CancellationTokenSource? _processingCts;
    private Task? _processingTask;

    private static readonly Dictionary<QualityType, (int width, int height, int quality)> QualityDimensions = new()
    {
        { QualityType.Thumbnail, (200, 200, 60) },    // Thumbnail
        { QualityType.Low, (800, 800, 70) },          // Mobile/web
        { QualityType.Medium, (1920, 1920, 85) },     // Desktop/email
        { QualityType.High, (3840, 3840, 95) }        // Print/download
    };

    public ImageProcessingService(
        IServiceProvider serviceProvider,
        IStorageProvider storageProvider,
        ILogger<ImageProcessingService> logger)
    {
        _serviceProvider = serviceProvider;
        _storageProvider = storageProvider;
        _logger = logger;
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

            // Create 4 processing items (one for each quality)
            var qualities = new[] { QualityType.Thumbnail, QualityType.Low, QualityType.Medium, QualityType.High };
            foreach (var quality in qualities)
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

    /// <summary>Process all pending queue items</summary>
    public async Task ProcessQueueAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var itemRepository = scope.ServiceProvider.GetRequiredService<IProcessingQueueItemRepository>();
            var photoRepository = scope.ServiceProvider.GetRequiredService<IPhotoRepository>();
            var urlService = scope.ServiceProvider.GetRequiredService<PhotoVersionUrlService>();

            var pendingItems = await itemRepository.GetPendingItemsAsync();
            var processedPhotos = new HashSet<Guid>();

            foreach (var item in pendingItems)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await ProcessQualityAsync(item, itemRepository, photoRepository, cancellationToken);
                    await itemRepository.MarkCompleteAsync(item.Id);
                    _logger.LogInformation("Completed processing photo {PhotoId} quality {Quality}", item.PhotoId, item.Quality);
                    processedPhotos.Add(item.PhotoId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing item {ItemId} (photo {PhotoId}, quality {Quality})", 
                        item.Id, item.PhotoId, item.Quality);
                    await itemRepository.MarkFailedAsync(item.Id, ex.Message);

                    // Increment retry if possible
                    if (item.CanRetry)
                    {
                        item.IncrementRetry(ex.Message);
                        await itemRepository.UpdateAsync(item);
                        await itemRepository.SaveChangesAsync();
                    }
                }
            }

            // Process items ready for retry
            var retryItems = await itemRepository.GetReadyForRetryAsync();
            foreach (var item in retryItems)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    item.Status = ProcessingStatus.Pending;
                    await itemRepository.UpdateAsync(item);
                    await itemRepository.SaveChangesAsync();

                    await ProcessQualityAsync(item, itemRepository, photoRepository, cancellationToken);
                    await itemRepository.MarkCompleteAsync(item.Id);
                    _logger.LogInformation("Retry succeeded for photo {PhotoId} quality {Quality} (attempt {Attempt})", 
                        item.PhotoId, item.Quality, item.RetryCount + 1);
                    processedPhotos.Add(item.PhotoId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Retry failed for item {ItemId}", item.Id);
                    await itemRepository.MarkFailedAsync(item.Id, ex.Message);

                    if (item.CanRetry)
                    {
                        item.IncrementRetry(ex.Message);
                        await itemRepository.UpdateAsync(item);
                        await itemRepository.SaveChangesAsync();
                    }
                }
            }

            // Generate pre-signed URLs for all processed photos
            foreach (var photoId in processedPhotos)
            {
                try
                {
                    // Check if all quality items are complete for this photo
                    var queueId = (await itemRepository.GetByPhotoIdAsync(photoId)).FirstOrDefault()?.ProcessingQueueId;
                    if (queueId.HasValue)
                    {
                        var allItems = await itemRepository.GetByQueueIdAsync(queueId.Value);
                        if (allItems.All(i => i.Status == ProcessingStatus.Complete))
                        {
                            _logger.LogDebug("All qualities complete for photo {PhotoId}. Generating pre-signed URLs...", photoId);
                            await urlService.GeneratePhotoVersionUrlsAsync(photoId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating pre-signed URLs for photo {PhotoId}", photoId);
                    // Don't fail the entire queue if URL generation fails
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProcessQueueAsync");
        }
    }

    /// <summary>Process a single quality version of a photo</summary>
    private async Task ProcessQualityAsync(ProcessingQueueItem item, IProcessingQueueItemRepository itemRepository, IPhotoRepository photoRepository, CancellationToken cancellationToken)
    {
        // Mark as processing
        item.Status = ProcessingStatus.Processing;
        await itemRepository.UpdateAsync(item);
        await itemRepository.SaveChangesAsync();

        // Get photo to find album ID
        var photo = await photoRepository.GetByIdAsync(item.PhotoId);
        if (photo == null)
            throw new FileNotFoundException($"Photo not found: {item.PhotoId}");

        // Get original from storage
        var originalPath = $"photogallery/{photo.AlbumId}/{item.PhotoId}/original.jpg";
        if (!await _storageProvider.ExistsAsync(originalPath))
            throw new FileNotFoundException($"Original photo not found at {originalPath}");

        using (var originalStream = await _storageProvider.DownloadAsync(originalPath))
        {
            if (originalStream == null)
                throw new InvalidOperationException($"Cannot read original photo from {originalPath}");

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

                _logger.LogInformation("Saved {Quality} version to {Path}", item.Quality, outputPath);

                // For public-viewing qualities (Thumbnail + Medium), also generate watermarked
                // variants. Reference: D009 (Watermark Pipeline). Public viewers (access-code
                // gallery, photo modal) see the watermarked Thumbnail/Medium; the unwatermarked
                // variants are delivered only via cart-checkout. Original/Low/High are never
                // watermarked here (defense in depth — Original ships only via paid checkout).
                if (item.Quality == QualityType.Medium || item.Quality == QualityType.Thumbnail)
                {
                    await GenerateWatermarkedVariantAsync(photo, item, image, quality, qualityName, cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Render and store a watermarked version of the just-resized image.
    /// Used for Thumbnail and Medium qualities — the variants public/guest viewers see.
    /// Failures are logged but do not fail the parent queue item — the unwatermarked
    /// variant is still useful, and the consistency checker will retry watermark generation.
    /// </summary>
    private async Task GenerateWatermarkedVariantAsync(
        Photo photo,
        ProcessingQueueItem item,
        Image image,
        int jpegQuality,
        string qualityName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var watermarkService = scope.ServiceProvider.GetRequiredService<WatermarkService>();

            // Encode the (already-resized) image into a fresh stream so the watermark
            // service can re-decode + apply watermark without mutating the original `image`.
            using var sourceStream = new MemoryStream();
            await image.SaveAsJpegAsync(sourceStream, new JpegEncoder { Quality = jpegQuality }, cancellationToken);
            sourceStream.Position = 0;

            using var watermarkedStream = new MemoryStream();
            // TODO (Phase 17b): pass the photographer's display name once user profile lands.
            // For now use a generic copyright text.
            var watermarkText = $"© {photo.UploadedBy ?? "Photo Gallery"}";
            await watermarkService.ApplyWatermarkAsync(
                sourceStream, watermarkedStream, watermarkText, jpegQuality, cancellationToken);
            watermarkedStream.Position = 0;

            var outputPath = $"photogallery/{photo.AlbumId}/{item.PhotoId}/{qualityName}-watermarked.jpg";
            await _storageProvider.UploadAsync(outputPath, watermarkedStream, "image/jpeg");

            _logger.LogInformation("Saved watermarked {Quality} variant to {Path}", item.Quality, outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to generate watermarked {Quality} for photo {PhotoId}; non-fatal — guest viewers will fall back to unwatermarked variant.",
                item.Quality, item.PhotoId);
        }
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

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Storage;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Service for processing photos with multiple compression levels
/// </summary>
public class ImageProcessingService : IImageProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IStorageProvider _storageProvider;
    private readonly ILogger<ImageProcessingService> _logger;
    private readonly List<CompressionProfile> _compressionProfiles;
    private CancellationTokenSource? _processingCts;
    private Task? _processingTask;

    private static readonly Dictionary<string, PhotoQuality> QualityMap = new()
    {
        { "high", PhotoQuality.HighCompression },
        { "medium", PhotoQuality.MediumCompression },
        { "low", PhotoQuality.LowCompression },
        { "raw", PhotoQuality.Raw }
    };

    public ImageProcessingService(
        IServiceProvider serviceProvider,
        IStorageProvider storageProvider,
        ILogger<ImageProcessingService> logger)
    {
        _serviceProvider = serviceProvider;
        _storageProvider = storageProvider;
        _logger = logger;

        _compressionProfiles = new List<CompressionProfile>
        {
            new CompressionProfile { Name = "high", QualityPercentage = 50, Description = "High compression (50% quality)" },
            new CompressionProfile { Name = "medium", QualityPercentage = 75, Description = "Medium compression (75% quality)" },
            new CompressionProfile { Name = "low", QualityPercentage = 85, Description = "Low compression (85% quality)" },
            new CompressionProfile { Name = "raw", QualityPercentage = 100, Description = "Raw quality (100% quality)" }
        };
    }

    public async Task<string> QueuePhotoAsync(string photoId)
    {
        if (!Guid.TryParse(photoId, out var photoGuid))
            throw new ArgumentException("Invalid photo ID format", nameof(photoId));

        using (var scope = _serviceProvider.CreateScope())
        {
            var queueRepo = scope.ServiceProvider.GetRequiredService<IProcessingQueueRepository>();
            var existing = await queueRepo.GetByPhotoIdAsync(photoGuid);
            if (existing != null)
            {
                _logger.LogInformation("Photo {PhotoId} is already queued", photoId);
                return existing.Id.ToString();
            }

            var queueEntry = new ProcessingQueue { PhotoId = photoGuid };
            await queueRepo.AddAsync(queueEntry);
            
            _logger.LogInformation("Photo {PhotoId} queued for processing", photoId);
            return queueEntry.Id.ToString();
        }
    }

    public async Task ProcessQueueAsync(CancellationToken cancellationToken = default)
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var queueRepo = scope.ServiceProvider.GetRequiredService<IProcessingQueueRepository>();
            var pending = await queueRepo.GetPendingAsync();
            
            foreach (var item in pending)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await ProcessPhotoAsync(item, scope, cancellationToken);
                    await queueRepo.MarkCompleteAsync(item.Id);
                    _logger.LogInformation("Photo {PhotoId} processing complete", item.PhotoId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing photo {PhotoId}", item.PhotoId);
                    await queueRepo.MarkFailedAsync(item.Id, ex.Message);
                }
            }
        }
    }

    public async Task<IEnumerable<PhotoVersion>> GetPhotoVersionsAsync(string photoId)
    {
        if (!Guid.TryParse(photoId, out var photoGuid))
            return Enumerable.Empty<PhotoVersion>();

        using (var scope = _serviceProvider.CreateScope())
        {
            var versionRepo = scope.ServiceProvider.GetRequiredService<IRepository<PhotoVersion>>();
            var versions = await versionRepo.GetAllAsync();
            return versions.Where(v => v.PhotoId == photoGuid).ToList();
        }
    }

    public async Task<PhotoVersion?> GetPhotoVersionAsync(string photoId, string quality)
    {
        var versions = await GetPhotoVersionsAsync(photoId);
        if (!QualityMap.TryGetValue(quality.ToLower(), out var photoQuality))
            return null;

        return versions.FirstOrDefault(v => v.Quality == photoQuality);
    }

    public IEnumerable<CompressionProfile> GetCompressionProfiles()
    {
        return _compressionProfiles;
    }

    public async Task StartProcessingWorkerAsync(CancellationToken applicationStopping)
    {
        _processingCts = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
        _processingTask = ProcessingWorkerAsync(_processingCts.Token);
        
        _logger.LogInformation("Image processing worker started");
        await Task.CompletedTask;
    }

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

    private async Task ProcessPhotoAsync(ProcessingQueue queueEntry, IServiceScope scope, CancellationToken cancellationToken)
    {
        var photoRepo = scope.ServiceProvider.GetRequiredService<IRepository<Photo>>();
        var photo = await photoRepo.GetByIdAsync(queueEntry.PhotoId);
        if (photo == null)
            throw new FileNotFoundException($"Photo {queueEntry.PhotoId} not found");

        // Mark processing as started
        photo.ProcessingStatus = PhotoProcessingStatus.Processing;
        photo.ProcessingStartedAt = DateTime.UtcNow;
        await photoRepo.UpdateAsync(photo);

        try
        {
            var originalStream = await _storageProvider.DownloadAsync(photo.StorageKey);
            using (var image = await Image.LoadAsync(originalStream, cancellationToken))
            {
                // Reset completion flags
                photo.HasThumbnail = false;
                photo.HasLow = false;
                photo.HasMedium = false;
                photo.HasHigh = false;

                foreach (var profile in _compressionProfiles)
                {
                    await ProcessQualityLevelAsync(photo, image, profile, scope, cancellationToken);
                }
            }

            // Mark processing as complete
            photo.ProcessingStatus = PhotoProcessingStatus.Complete;
            photo.ProcessingCompletedAt = DateTime.UtcNow;
            photo.ProcessingComplete = true; // Legacy field for compatibility
            await photoRepo.UpdateAsync(photo);
        }
        catch (Exception ex)
        {
            photo.ProcessingStatus = PhotoProcessingStatus.Failed;
            photo.ProcessingCompletedAt = DateTime.UtcNow;
            await photoRepo.UpdateAsync(photo);
            throw;
        }
    }

    private async Task ProcessQualityLevelAsync(
        Photo photo,
        Image image,
        CompressionProfile profile,
        IServiceScope scope,
        CancellationToken cancellationToken)
    {
        using (var processedImage = image.Clone(ctx => ctx.AutoOrient()))
        {
            var memoryStream = new MemoryStream();
            var jpegEncoder = new JpegEncoder { Quality = profile.QualityPercentage };
            
            await processedImage.SaveAsync(memoryStream, jpegEncoder, cancellationToken);
            memoryStream.Position = 0;

            var storageKey = $"photogallery/{photo.AlbumId}/{photo.Id}/{profile.Name}.jpg";
            await _storageProvider.UploadAsync(storageKey, memoryStream, "image/jpeg");

            if (!QualityMap.TryGetValue(profile.Name, out var photoQuality))
                return;

            var versionRepo = scope.ServiceProvider.GetRequiredService<IRepository<PhotoVersion>>();
            var photoVersion = new PhotoVersion
            {
                PhotoId = photo.Id,
                Quality = photoQuality,
                FileSize = memoryStream.Length,
                StorageKey = storageKey,
                ProcessedDate = DateTime.UtcNow
            };

            await versionRepo.AddAsync(photoVersion);

            // Update photo flags based on quality level
            var photoRepo = scope.ServiceProvider.GetRequiredService<IRepository<Photo>>();
            switch (profile.Name)
            {
                case "high":
                    photo.HasHigh = true;
                    break;
                case "medium":
                    photo.HasMedium = true;
                    break;
                case "low":
                    photo.HasLow = true;
                    break;
                case "raw":
                    // For thumbnail generation, we'll add that in Phase 15
                    break;
            }
            await photoRepo.UpdateAsync(photo);
            
            _logger.LogInformation(
                "Created {Quality} version of photo {PhotoId}, size: {Size} bytes",
                profile.Name, photo.Id, memoryStream.Length);
        }
    }
}

using PhotoGallery.Models;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Service for processing photos with multiple compression levels
/// </summary>
public interface IImageProcessor
{
    /// <summary>
    /// Queue a photo for processing
    /// </summary>
    /// <param name="photoId">Photo ID to process</param>
    /// <returns>Processing queue entry ID</returns>
    Task<string> QueuePhotoAsync(string photoId);

    /// <summary>
    /// Process all pending photos in the queue
    /// Runs asynchronously and can be called repeatedly
    /// </summary>
    Task ProcessQueueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all processed versions of a photo
    /// </summary>
    /// <param name="photoId">Photo ID</param>
    /// <returns>List of PhotoVersion objects for all quality levels</returns>
    Task<IEnumerable<PhotoVersion>> GetPhotoVersionsAsync(string photoId);

    /// <summary>
    /// Get a specific version of a photo
    /// </summary>
    /// <param name="photoId">Photo ID</param>
    /// <param name="quality">Quality level (high, medium, low, raw)</param>
    /// <returns>PhotoVersion or null if not found</returns>
    Task<PhotoVersion?> GetPhotoVersionAsync(string photoId, string quality);

    /// <summary>
    /// Get compression profiles configuration
    /// </summary>
    /// <returns>List of available compression profiles</returns>
    IEnumerable<CompressionProfile> GetCompressionProfiles();

    /// <summary>
    /// Start the background processing worker
    /// </summary>
    Task StartProcessingWorkerAsync(CancellationToken applicationStopping);
}

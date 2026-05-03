using PhotoGallery.Models;

namespace PhotoGallery.Interfaces;

/// <summary>
/// Repository for accessing processing queue data
/// Reference: D003 (Image Processing with Compression Profiles)
/// </summary>
public interface IProcessingQueueRepository : IRepository<ProcessingQueue>
{
    /// <summary>
    /// Get all pending processing jobs
    /// </summary>
    Task<IEnumerable<ProcessingQueue>> GetPendingAsync();

    /// <summary>
    /// Get processing job by photo ID
    /// </summary>
    Task<ProcessingQueue?> GetByPhotoIdAsync(Guid photoId);

    /// <summary>
    /// Mark a job as completed
    /// </summary>
    Task MarkCompleteAsync(Guid processingQueueId);

    /// <summary>
    /// Mark a job as failed with error message
    /// </summary>
    Task MarkFailedAsync(Guid processingQueueId, string errorMessage);
}

using PhotoGallery.Models;

namespace PhotoGallery.Interfaces;

/// <summary>
/// Repository for accessing processing queue data
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
    Task MarkCompleteAsync(string processingQueueId);

    /// <summary>
    /// Mark a job as failed with error message
    /// </summary>
    Task MarkFailedAsync(string processingQueueId, string errorMessage);
}

using PhotoGallery.Models;
using PhotoGallery.Interfaces;

namespace PhotoGallery.Data.Repositories;

/// <summary>
/// Repository interface for ProcessingQueueItem operations.
/// Reference: D003 (Image Processing with Compression Profiles)
/// </summary>
public interface IProcessingQueueItemRepository : IRepository<ProcessingQueueItem>
{
    /// <summary>Get all pending processing items</summary>
    Task<IEnumerable<ProcessingQueueItem>> GetPendingItemsAsync();

    /// <summary>Get all items ready for retry (NextRetryTime has passed, status is Error)</summary>
    Task<IEnumerable<ProcessingQueueItem>> GetReadyForRetryAsync();

    /// <summary>Get all items for a specific processing queue</summary>
    Task<IEnumerable<ProcessingQueueItem>> GetByQueueIdAsync(Guid queueId);

    /// <summary>Get all items for a specific photo</summary>
    Task<IEnumerable<ProcessingQueueItem>> GetByPhotoIdAsync(Guid photoId);

    /// <summary>Mark an item as completed</summary>
    Task MarkCompleteAsync(Guid itemId);

    /// <summary>Mark an item as failed with an error message</summary>
    Task MarkFailedAsync(Guid itemId, string errorMessage);
}

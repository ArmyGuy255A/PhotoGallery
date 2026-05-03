using Microsoft.EntityFrameworkCore;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Repositories;

/// <summary>
/// Repository implementation for ProcessingQueueItem operations.
/// Reference: D003 (Image Processing with Compression Profiles)
/// </summary>
public class ProcessingQueueItemRepository : Repository<ProcessingQueueItem>, IProcessingQueueItemRepository
{
    public ProcessingQueueItemRepository(ApplicationDbContext context) : base(context)
    {
    }

    /// <summary>Get all pending processing items</summary>
    public async Task<IEnumerable<ProcessingQueueItem>> GetPendingItemsAsync()
    {
        return await _dbSet
            .Where(item => item.Status == ProcessingStatus.Pending)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
    }

    /// <summary>Get all items ready for retry (NextRetryTime has passed, status is Error)</summary>
    public async Task<IEnumerable<ProcessingQueueItem>> GetReadyForRetryAsync()
    {
        var now = DateTime.UtcNow;
        return await _dbSet
            .Where(item => item.Status == ProcessingStatus.Error && 
                          item.NextRetryTime.HasValue &&
                          item.NextRetryTime.Value <= now &&
                          item.RetryCount < item.MaxRetries) // Don't use computed property in query
            .OrderBy(item => item.NextRetryTime)
            .ToListAsync();
    }

    /// <summary>Get all items for a specific processing queue</summary>
    public async Task<IEnumerable<ProcessingQueueItem>> GetByQueueIdAsync(Guid queueId)
    {
        return await _dbSet
            .Where(item => item.ProcessingQueueId == queueId)
            .OrderBy(item => item.Quality)
            .ToListAsync();
    }

    /// <summary>Get all items for a specific photo</summary>
    public async Task<IEnumerable<ProcessingQueueItem>> GetByPhotoIdAsync(Guid photoId)
    {
        return await _dbSet
            .Where(item => item.PhotoId == photoId)
            .OrderBy(item => item.Quality)
            .ToListAsync();
    }

    /// <summary>Mark an item as completed</summary>
    public async Task MarkCompleteAsync(Guid itemId)
    {
        var item = await _dbSet.FindAsync(itemId);
        if (item != null)
        {
            item.Status = ProcessingStatus.Complete;
            item.CompletedAt = DateTime.UtcNow;
            item.UpdatedAt = DateTime.UtcNow;
            _dbSet.Update(item);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>Mark an item as failed with an error message</summary>
    public async Task MarkFailedAsync(Guid itemId, string errorMessage)
    {
        var item = await _dbSet.FindAsync(itemId);
        if (item != null)
        {
            item.Status = ProcessingStatus.Error;
            item.LastError = errorMessage;
            item.UpdatedAt = DateTime.UtcNow;
            _dbSet.Update(item);
            await _context.SaveChangesAsync();
        }
    }
}

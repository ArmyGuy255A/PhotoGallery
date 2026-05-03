using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Repositories;

/// <summary>
/// Repository for ProcessingQueue entities.
/// Reference: D003 (Image Processing with Compression Profiles)
/// </summary>
public class ProcessingQueueRepository : Repository<ProcessingQueue>, IProcessingQueueRepository
{
    private new readonly ApplicationDbContext _context; // Use 'new' to shadow base class _context

    public ProcessingQueueRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IEnumerable<ProcessingQueue>> GetPendingAsync()
    {
        return await _context.ProcessingQueues
            .Where(pq => pq.Status == ProcessingStatus.Pending)
            .Include(pq => pq.Items)
            .OrderBy(pq => pq.CreatedAt)
            .ToListAsync();
    }

    public async Task<ProcessingQueue?> GetByPhotoIdAsync(Guid photoId)
    {
        return await _context.ProcessingQueues
            .Include(pq => pq.Items)
            .FirstOrDefaultAsync(pq => pq.PhotoId == photoId);
    }

    public async Task MarkCompleteAsync(Guid processingQueueId)
    {
        var item = await _context.ProcessingQueues.FindAsync(processingQueueId);
        if (item != null)
        {
            item.MarkComplete();
            await _context.SaveChangesAsync();
        }
    }

    public async Task MarkFailedAsync(Guid processingQueueId, string errorMessage)
    {
        var item = await _context.ProcessingQueues.FindAsync(processingQueueId);
        if (item != null)
        {
            item.MarkError(errorMessage);
            await _context.SaveChangesAsync();
        }
    }
}

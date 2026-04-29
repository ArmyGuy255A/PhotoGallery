using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Repositories;

public class ProcessingQueueRepository : Repository<ProcessingQueue>, IProcessingQueueRepository
{
    private readonly ApplicationDbContext _context;

    public ProcessingQueueRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<IEnumerable<ProcessingQueue>> GetPendingAsync()
    {
        return await _context.ProcessingQueues
            .Where(pq => pq.Status == ProcessingStatus.Pending)
            .OrderBy(pq => pq.QueuedDate)
            .ToListAsync();
    }

    public async Task<ProcessingQueue?> GetByPhotoIdAsync(Guid photoId)
    {
        return await _context.ProcessingQueues
            .FirstOrDefaultAsync(pq => pq.PhotoId == photoId);
    }

    public async Task MarkCompleteAsync(string processingQueueId)
    {
        var item = await _context.ProcessingQueues.FindAsync(processingQueueId);
        if (item != null)
        {
            item.Status = ProcessingStatus.Complete;
            item.ProcessedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task MarkFailedAsync(string processingQueueId, string errorMessage)
    {
        var item = await _context.ProcessingQueues.FindAsync(processingQueueId);
        if (item != null)
        {
            item.Status = ProcessingStatus.Error;
            item.ErrorMessage = errorMessage;
            item.ProcessedDate = DateTime.UtcNow;
            item.RetryCount++;
            await _context.SaveChangesAsync();
        }
    }
}

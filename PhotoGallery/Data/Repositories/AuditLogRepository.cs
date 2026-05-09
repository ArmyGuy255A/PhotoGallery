using Microsoft.EntityFrameworkCore;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly ApplicationDbContext _context;

    public AuditLogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddEntryAsync(AuditLogEntry entry)
    {
        await _context.AuditLogEntries.AddAsync(entry);
        await _context.SaveChangesAsync();
    }

    public async Task<List<AuditLogEntry>> GetRecentAsync(int skip, int take)
    {
        return await _context.AuditLogEntries
            .OrderByDescending(a => a.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }
}

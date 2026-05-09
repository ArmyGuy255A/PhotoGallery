using PhotoGallery.Models;

namespace PhotoGallery.Interfaces;

public interface IAuditLogRepository
{
    Task AddEntryAsync(AuditLogEntry entry);
    Task<List<AuditLogEntry>> GetRecentAsync(int skip, int take);
}

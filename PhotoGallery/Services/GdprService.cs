using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Services;

/// <summary>
/// GDPR compliance service. Provides:
/// 1. Right to data portability — <see cref="ExportUserDataAsync"/> assembles a JSON
///    snapshot of all user-related rows.
/// 2. Right to erasure — <see cref="DeleteUserAsync"/> performs a hard cascade delete
///    of the user and everything they own, inside a single transaction (when the
///    underlying provider supports it). Audit log entries describing the deletion
///    are written after the cascade completes.
///
/// Storage cleanup (blob deletion in MinIO/S3) is intentionally out of scope here:
/// a "user.deletion.storage-pending" audit entry is written, listing the storage keys
/// that a future background worker should purge.
/// </summary>
public class GdprService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditLogRepository _auditLog;
    private readonly ILogger<GdprService> _logger;

    public GdprService(
        ApplicationDbContext db,
        IAuditLogRepository auditLog,
        ILogger<GdprService> logger)
    {
        _db = db;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<UserDataExport> ExportUserDataAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("userId is required", nameof(userId));

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found");

        var ownedAlbums = await _db.Albums.AsNoTracking()
            .Where(a => a.OwnerId == userId)
            .Select(a => new AlbumExport
            {
                Id = a.Id,
                Title = a.Title,
                Description = a.Description,
                CreatedDate = a.CreatedDate,
                PhotoCount = a.Photos.Count
            })
            .ToListAsync();

        var albumIds = ownedAlbums.Select(a => a.Id).ToList();

        var uploadedPhotos = await _db.Photos.AsNoTracking()
            .Where(p => albumIds.Contains(p.AlbumId) || p.UploadedBy == userId)
            .Select(p => new PhotoExport
            {
                Id = p.Id,
                AlbumId = p.AlbumId,
                FileName = p.FileName,
                StorageKey = p.StorageKey,
                UploadDate = p.UploadDate,
                Metadata = p.Metadata
            })
            .ToListAsync();

        var accessCodes = await _db.AccessCodes.AsNoTracking()
            .Where(ac => (ac.AlbumId != null && albumIds.Contains(ac.AlbumId.Value)) || ac.CreatedBy == userId)
            .Select(ac => new AccessCodeExport
            {
                Id = ac.Id,
                AlbumId = ac.AlbumId,
                Code = ac.Code,
                CreatedDate = ac.CreatedDate,
                ExpirationDate = ac.ExpirationDate
            })
            .ToListAsync();

        var photoIds = uploadedPhotos.Select(p => p.Id).ToList();
        var downloads = await _db.Downloads.AsNoTracking()
            .Where(d => photoIds.Contains(d.PhotoId))
            .Select(d => new DownloadExport
            {
                Id = d.Id,
                PhotoId = d.PhotoId,
                AccessCodeId = d.AccessCodeId,
                Quality = d.Quality.ToString(),
                DownloadedAt = d.DownloadedAt
            })
            .ToListAsync();

        return new UserDataExport
        {
            ExportTimestamp = DateTime.UtcNow,
            SchemaVersion = "1.0",
            Profile = new ProfileExport
            {
                Id = user.Id,
                Email = user.Email,
                UserName = user.UserName,
                FirstName = user.FirstName,
                LastName = user.LastName,
                CreatedDate = user.CreatedDate,
                IsActive = user.IsActive
            },
            OwnedAlbums = ownedAlbums,
            UploadedPhotos = uploadedPhotos,
            SavedAccessCodes = accessCodes,
            Downloads = downloads
        };
    }

    public async Task DeleteUserAsync(string userId, string actorEmail)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("userId is required", nameof(userId));
        if (string.IsNullOrEmpty(actorEmail))
            throw new ArgumentException("actorEmail is required", nameof(actorEmail));

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found");

        // Capture identifiers BEFORE deletion so the audit log can record what was removed.
        var albumIds = await _db.Albums
            .Where(a => a.OwnerId == userId)
            .Select(a => a.Id)
            .ToListAsync();

        var photos = await _db.Photos
            .Where(p => albumIds.Contains(p.AlbumId))
            .Select(p => new { p.Id, p.StorageKey })
            .ToListAsync();
        var photoIds = photos.Select(p => p.Id).ToList();
        var storageKeys = photos.Select(p => p.StorageKey).ToList();

        var accessCodeIds = await _db.AccessCodes
            .Where(ac => ac.AlbumId != null && albumIds.Contains(ac.AlbumId.Value))
            .Select(ac => ac.Id)
            .ToListAsync();

        var userEmail = user.Email ?? string.Empty;

        // Use a relational transaction when the provider supports it (SQLite/SqlServer);
        // skip for the in-memory provider used in tests.
        var useTx = _db.Database.IsRelational();
        var tx = useTx ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            // Explicit delete order (works on InMemory + relational, independent of FK cascade
            // configuration) -- leaf children first, parents last.
            if (photoIds.Count > 0)
            {
                _db.Downloads.RemoveRange(_db.Downloads.Where(d => photoIds.Contains(d.PhotoId)));
                _db.PhotoVersionUrls.RemoveRange(_db.PhotoVersionUrls.Where(u => photoIds.Contains(u.PhotoId)));
                _db.PhotoVersions.RemoveRange(_db.PhotoVersions.Where(v => photoIds.Contains(v.PhotoId)));
                _db.PhotoFiles.RemoveRange(_db.PhotoFiles.Where(f => photoIds.Contains(f.PhotoId)));
                _db.ProcessingQueueItems.RemoveRange(_db.ProcessingQueueItems.Where(i => photoIds.Contains(i.PhotoId)));
                _db.ProcessingQueues.RemoveRange(_db.ProcessingQueues.Where(q => photoIds.Contains(q.PhotoId)));
            }

            if (accessCodeIds.Count > 0)
            {
                _db.UserAccessLogs.RemoveRange(_db.UserAccessLogs.Where(l => accessCodeIds.Contains(l.AccessCodeId)));
                _db.AccessCodes.RemoveRange(_db.AccessCodes.Where(ac => accessCodeIds.Contains(ac.Id)));
            }

            // UserAccessLogs that reference this user via UserId (different from AccessCode-based ones)
            _db.UserAccessLogs.RemoveRange(_db.UserAccessLogs.Where(l => l.UserId == userId));

            if (photoIds.Count > 0)
                _db.Photos.RemoveRange(_db.Photos.Where(p => photoIds.Contains(p.Id)));

            if (albumIds.Count > 0)
                _db.Albums.RemoveRange(_db.Albums.Where(a => albumIds.Contains(a.Id)));

            _db.Users.Remove(user);

            await _db.SaveChangesAsync();

            if (tx != null) await tx.CommitAsync();
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }

        // Write audit log entries AFTER the cascade transaction has succeeded so we
        // never record a deletion that didn't actually happen.
        var deletionDetails = JsonSerializer.Serialize(new
        {
            albumCount = albumIds.Count,
            photoCount = photoIds.Count,
            accessCodeCount = accessCodeIds.Count,
            deletedUserEmail = userEmail
        });

        await _auditLog.AddEntryAsync(new AuditLogEntry
        {
            Action = "user.deleted",
            ActorUserId = userId,
            ActorEmail = actorEmail,
            TargetType = nameof(User),
            TargetId = userId,
            Details = deletionDetails
        });

        if (storageKeys.Count > 0)
        {
            await _auditLog.AddEntryAsync(new AuditLogEntry
            {
                Action = "user.deletion.storage-pending",
                ActorUserId = userId,
                ActorEmail = actorEmail,
                TargetType = nameof(User),
                TargetId = userId,
                Details = JsonSerializer.Serialize(new { storageKeys })
            });
        }

        _logger.LogInformation(
            "GDPR delete completed for user {UserId} by {ActorEmail}: {AlbumCount} albums, {PhotoCount} photos, {AccessCodeCount} access codes",
            userId, actorEmail, albumIds.Count, photoIds.Count, accessCodeIds.Count);
    }
}

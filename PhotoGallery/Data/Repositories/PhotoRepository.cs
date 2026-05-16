using Microsoft.EntityFrameworkCore;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Repositories;

public class PhotoRepository : Repository<Photo>, IPhotoRepository
{
    public PhotoRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<Photo>> GetAlbumPhotosAsync(Guid albumId)
    {
        // Filter out photos still in Uploading state — the SPA has a write SAS
        // but hasn't yet called /upload-complete, so the blob may not exist
        // and the photo has no processing artifacts. Showing these to the
        // user produces ghost rows. Reference: Phase 2 plan,
        // PhotoProcessingStatus.Uploading.
        return await _dbSet
            .Where(p => p.AlbumId == albumId && p.ProcessingStatus != PhotoProcessingStatus.Uploading)
            .OrderByDescending(p => p.UploadDate)
            .ToListAsync();
    }

    public async Task<Photo?> GetWithVersionsAsync(Guid photoId)
    {
        return await _dbSet
            .Include(p => p.PhotoVersions)
            .FirstOrDefaultAsync(p => p.Id == photoId);
    }

    public async Task<List<Photo>> GetUnprocessedPhotosAsync()
    {
        return await _dbSet
            .Where(p => !p.ProcessingComplete)
            .ToListAsync();
    }

    public async Task<HashSet<string>> GetExistingFileNamesAsync(Guid albumId)
    {
        // Case-sensitive HashSet to match the storage layer; the duplicate
        // check intentionally uses OrdinalIgnoreCase on the caller side so
        // we don't depend on the database collation. Includes Uploading
        // rows so concurrent tabs cannot both reserve the same name. The
        // orphan reaper releases names from abandoned tickets after the
        // grace window expires.
        var names = await _dbSet
            .Where(p => p.AlbumId == albumId)
            .Select(p => p.FileName)
            .ToListAsync();
        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }
    public async Task<Dictionary<string, ExistingPhotoSummary>> GetExistingPhotoSummariesByNameAsync(Guid albumId)
    {
        // Pull just the columns we need and project to the public record
        // struct. OrdinalIgnoreCase mirrors GetExistingFileNamesAsync so both
        // upload paths agree on what counts as a duplicate, regardless of the
        // database collation. Last-write-wins on case-only collisions, which
        // matches the behavior of the unique filtered index in
        // UniquePhotoFileNamePerAlbum (case-insensitive collation in
        // SqlServer; this client-side dedup keeps Sqlite consistent).
        var rows = await _dbSet
            .Where(p => p.AlbumId == albumId)
            .Select(p => new { p.FileName, p.Id, p.ProcessingStatus })
            .ToListAsync();
        var map = new Dictionary<string, ExistingPhotoSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
            map[r.FileName] = new ExistingPhotoSummary(r.Id, r.ProcessingStatus);
        return map;
    }
}
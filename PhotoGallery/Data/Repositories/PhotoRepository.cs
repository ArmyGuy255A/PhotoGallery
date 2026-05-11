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
}

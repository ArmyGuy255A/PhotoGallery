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
        return await _dbSet
            .Where(p => p.AlbumId == albumId)
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

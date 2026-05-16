using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Repositories;

public class PhotoVersionUrlRepository : Repository<PhotoVersionUrl>, IPhotoVersionUrlRepository
{
    public PhotoVersionUrlRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<PhotoVersionUrl?> GetByPhotoAndQualityAsync(Guid photoId, QualityType quality)
    {
        return await _dbSet
            .Where(pvu => pvu.PhotoId == photoId && pvu.Quality == quality && pvu.IsActive)
            .FirstOrDefaultAsync();
    }

    public async Task<PhotoVersionUrl?> GetByPhotoAndQualityIncludingInactiveAsync(Guid photoId, QualityType quality)
    {
        return await _dbSet
            .Where(pvu => pvu.PhotoId == photoId && pvu.Quality == quality)
            .FirstOrDefaultAsync();
    }

    public async Task<List<PhotoVersionUrl>> GetByPhotoIdAsync(Guid photoId)
    {
        return await _dbSet
            .Where(pvu => pvu.PhotoId == photoId && pvu.IsActive)
            .ToListAsync();
    }

    public async Task<List<PhotoVersionUrl>> GetByPhotoIdsAsync(IEnumerable<Guid> photoIds)
    {
        var ids = photoIds as IList<Guid> ?? photoIds.ToList();
        if (ids.Count == 0) return new List<PhotoVersionUrl>();
        return await _dbSet
            .AsNoTracking()
            .Where(pvu => ids.Contains(pvu.PhotoId) && pvu.IsActive)
            .ToListAsync();
    }

    public async Task<List<PhotoVersionUrl>> GetExpiringAsync(DateTime beforeDate)
    {
        return await _dbSet
            .Where(pvu => pvu.IsActive && pvu.ExpiresAt < beforeDate)
            .ToListAsync();
    }

    public async Task<List<PhotoVersionUrl>> GetExpiredAsync()
    {
        return await _dbSet
            .Where(pvu => pvu.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();
    }

    public async Task InvalidateByPhotoIdAsync(Guid photoId)
    {
        var urls = await _dbSet
            .Where(pvu => pvu.PhotoId == photoId && pvu.IsActive)
            .ToListAsync();

        foreach (var url in urls)
        {
            url.IsActive = false;
        }

        if (urls.Count > 0)
        {
            await SaveChangesAsync();
        }
    }

    public async Task InvalidateByAlbumIdAsync(Guid albumId)
    {
        var urls = await _dbSet
            .Where(pvu => pvu.Photo.AlbumId == albumId && pvu.IsActive)
            .ToListAsync();

        foreach (var url in urls)
        {
            url.IsActive = false;
        }

        if (urls.Count > 0)
        {
            await SaveChangesAsync();
        }
    }
}

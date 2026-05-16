using Microsoft.EntityFrameworkCore;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Repositories;

public class AccessCodeRepository : Repository<AccessCode>, IAccessCodeRepository
{
    public AccessCodeRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<AccessCode?> GetByCodeAsync(string code)
    {
        return await _dbSet
            .Include(ac => ac.Album)
            .FirstOrDefaultAsync(ac => ac.Code == code && !ac.IsDeleted);
    }

    public async Task<List<AccessCode>> GetAlbumCodesAsync(Guid albumId)
    {
        return await _dbSet
            .Where(ac => ac.AlbumId == albumId && !ac.IsDeleted)
            .OrderByDescending(ac => ac.CreatedDate)
            .ToListAsync();
    }

    public async Task<List<AccessCode>> GetValidCodesAsync(Guid albumId)
    {
        return await _dbSet
            .Where(ac => ac.AlbumId == albumId && !ac.IsDeleted &&
                         (ac.ExpirationDate == null || ac.ExpirationDate > DateTime.UtcNow))
            .OrderByDescending(ac => ac.CreatedDate)
            .ToListAsync();
    }

    public async Task<bool> IsCodeValidAsync(string code)
    {
        return await _dbSet
            .AnyAsync(ac => ac.Code == code && !ac.IsDeleted &&
                           (ac.ExpirationDate == null || ac.ExpirationDate > DateTime.UtcNow));
    }
}

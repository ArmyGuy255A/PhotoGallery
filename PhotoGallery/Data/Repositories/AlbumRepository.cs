using Microsoft.EntityFrameworkCore;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Repositories;

public class AlbumRepository : Repository<Album>, IAlbumRepository
{
    public AlbumRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<Album>> GetUserAlbumsAsync(Guid userId)
    {
        return await _dbSet
            .Where(a => a.OwnerId == userId.ToString())
            .OrderByDescending(a => a.CreatedDate)
            .ToListAsync();
    }

    public async Task<Album?> GetByIdWithPhotosAsync(Guid albumId)
    {
        return await _dbSet
            .Include(a => a.Photos)
            .FirstOrDefaultAsync(a => a.Id == albumId);
    }

    public async Task<Album?> GetByIdWithAccessCodesAsync(Guid albumId)
    {
        return await _dbSet
            .Include(a => a.AccessCodes)
            .FirstOrDefaultAsync(a => a.Id == albumId);
    }

    public async Task<AccessCode?> GetAccessCodeByCodeAsync(string code)
    {
        return await _context.Set<AccessCode>()
            .Include(ac => ac.Album)
            .FirstOrDefaultAsync(ac => ac.Code == code);
    }

    public async Task<bool> AccessCodeExistsAsync(string code)
    {
        return await _context.Set<AccessCode>()
            .AnyAsync(ac => ac.Code == code);
    }
}

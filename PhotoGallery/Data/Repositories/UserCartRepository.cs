using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Repositories;

/// <inheritdoc cref="IUserCartRepository"/>
public class UserCartRepository : IUserCartRepository
{
    private readonly ApplicationDbContext _context;

    public UserCartRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<UserCartItem>> GetForUserAsync(string userId)
    {
        return await _context.UserCartItems
            .Where(c => c.UserId == userId)
            .Include(c => c.Photo)
            .Include(c => c.SourceAlbum)
            .OrderByDescending(c => c.AddedAt)
            .ToListAsync();
    }

    public async Task<UserCartItem> AddAsync(UserCartItem item)
    {
        // Check-then-insert (mirrors SaveAccessCode pattern). We rely on the unique
        // index as a backstop — a concurrent add could still race past the check,
        // in which case SaveChangesAsync will surface DbUpdateException to the caller.
        var existing = await _context.UserCartItems
            .FirstOrDefaultAsync(c =>
                c.UserId == item.UserId &&
                c.PhotoId == item.PhotoId &&
                c.Quality == item.Quality);

        if (existing != null)
        {
            return existing;
        }

        await _context.UserCartItems.AddAsync(item);
        return item;
    }

    public async Task<bool> RemoveAsync(string userId, Guid photoId, QualityType quality)
    {
        var existing = await _context.UserCartItems
            .FirstOrDefaultAsync(c =>
                c.UserId == userId &&
                c.PhotoId == photoId &&
                c.Quality == quality);

        if (existing == null)
        {
            return false;
        }

        _context.UserCartItems.Remove(existing);
        return true;
    }

    public async Task<int> ClearAsync(string userId)
    {
        var rows = await _context.UserCartItems
            .Where(c => c.UserId == userId)
            .ToListAsync();
        _context.UserCartItems.RemoveRange(rows);
        return rows.Count;
    }

    public Task<int> CountForUserAsync(string userId)
    {
        return _context.UserCartItems.CountAsync(c => c.UserId == userId);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}

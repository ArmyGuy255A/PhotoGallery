using PhotoGallery.Enums;
using PhotoGallery.Models;

namespace PhotoGallery.Interfaces;

/// <summary>
/// Per-user shopping cart repository. Callers are responsible for invoking
/// <see cref="IRepository{T}.SaveChangesAsync"/> after mutating operations —
/// nothing here calls SaveChanges implicitly.
/// </summary>
public interface IUserCartRepository
{
    /// <summary>
    /// Returns every cart item for the given user, eagerly including
    /// <see cref="UserCartItem.Photo"/> and <see cref="UserCartItem.SourceAlbum"/>
    /// so the controller can build the list response without N+1 queries.
    /// </summary>
    Task<List<UserCartItem>> GetForUserAsync(string userId);

    /// <summary>
    /// Adds <paramref name="item"/>. Idempotent on the unique
    /// (UserId, PhotoId, Quality) index — if a matching row already exists,
    /// returns the existing row and does NOT add a duplicate.
    /// Mirrors <c>AccountController.SaveAccessCode</c>'s check-then-insert pattern.
    /// </summary>
    /// <returns>The persisted (or pre-existing) row.</returns>
    Task<UserCartItem> AddAsync(UserCartItem item);

    /// <summary>Removes the row for (userId, photoId, quality) if it exists. Returns true if a row was removed.</summary>
    Task<bool> RemoveAsync(string userId, Guid photoId, QualityType quality);

    /// <summary>Removes every row for the given user. Returns the number of rows queued for deletion.</summary>
    Task<int> ClearAsync(string userId);

    /// <summary>Counts rows for the given user — used by the cap check.</summary>
    Task<int> CountForUserAsync(string userId);

    /// <summary>Persists pending changes via the underlying DbContext.</summary>
    Task SaveChangesAsync();
}

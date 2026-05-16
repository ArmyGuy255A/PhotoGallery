using PhotoGallery.Models;
using PhotoGallery.Enums;

namespace PhotoGallery.Interfaces;

public interface IPhotoVersionUrlRepository : IRepository<PhotoVersionUrl>
{
    /// <summary>
    /// Get cached URL for a specific photo and quality.
    /// Returns null if not found or inactive.
    /// </summary>
    Task<PhotoVersionUrl?> GetByPhotoAndQualityAsync(Guid photoId, QualityType quality);

    /// <summary>
    /// Get the row for a (photoId, quality) pair regardless of <see cref="PhotoVersionUrl.IsActive"/>.
    ///
    /// Used by the cache-write/upsert path so it can find and overwrite an existing inactive row
    /// rather than inserting a sibling row, which would violate the unique
    /// (PhotoId, Quality) index defined in <c>PhotoVersionUrlConfiguration</c>.
    ///
    /// Reference: D008 (Cached Pre-Signed URL Storage Verification).
    /// </summary>
    Task<PhotoVersionUrl?> GetByPhotoAndQualityIncludingInactiveAsync(Guid photoId, QualityType quality);
    
    /// <summary>
    /// Get all URLs for a photo.
    /// </summary>
    Task<List<PhotoVersionUrl>> GetByPhotoIdAsync(Guid photoId);

    /// <summary>
    /// Batch variant — fetch all active cached URLs for the supplied photo IDs in
    /// one round-trip. Used by the album-list / paged-photos endpoints so a 20-photo
    /// page is two DB calls (Photos + PhotoVersionUrls) instead of 40 individual
    /// lookups. The caller groups the result by PhotoId in memory.
    /// </summary>
    Task<List<PhotoVersionUrl>> GetByPhotoIdsAsync(IEnumerable<Guid> photoIds);
    
    /// <summary>
    /// Get all active URLs that are expiring soon (before the refresh window).
    /// Used by PhotoVersionUrlRefreshWorker to refresh URLs before they expire.
    /// </summary>
    Task<List<PhotoVersionUrl>> GetExpiringAsync(DateTime beforeDate);
    
    /// <summary>
    /// Get all expired URLs (for cleanup/logging).
    /// </summary>
    Task<List<PhotoVersionUrl>> GetExpiredAsync();
    
    /// <summary>
    /// Invalidate all URLs for a photo (e.g., when photo is deleted or album access revoked).
    /// </summary>
    Task InvalidateByPhotoIdAsync(Guid photoId);
    
    /// <summary>
    /// Invalidate all URLs for an album.
    /// </summary>
    Task InvalidateByAlbumIdAsync(Guid albumId);
}

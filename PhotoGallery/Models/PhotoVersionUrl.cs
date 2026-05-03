using PhotoGallery.Enums;

namespace PhotoGallery.Models;

/// <summary>
/// Represents a cached pre-signed MinIO URL for a specific photo quality version.
/// Used to offload downloads directly to MinIO blob storage without routing through web server.
/// 
/// Reference: D004 (Pre-Signed URL Caching Architecture)
/// 
/// URLs are generated during photo processing and cached with a TTL.
/// Only Thumbnail and Medium qualities are cached; Low and High generated on-demand (shopping cart).
/// Background service (PhotoVersionUrlRefreshWorker) refreshes URLs when approaching expiration.
/// </summary>
public class PhotoVersionUrl
{
    public Guid Id { get; set; }

    /// <summary>Foreign key to Photo</summary>
    public Guid PhotoId { get; set; }
    
    /// <summary>Quality level: Thumbnail, Low, Medium, or High</summary>
    public QualityType Quality { get; set; }
    
    /// <summary>Pre-signed MinIO URL for direct download. Encrypted when stored in database.</summary>
    public string PresignedUrl { get; set; } = null!;
    
    /// <summary>UTC DateTime when this URL expires and should be regenerated</summary>
    public DateTime ExpiresAt { get; set; }
    
    /// <summary>UTC DateTime when this URL was generated</summary>
    public DateTime GeneratedAt { get; set; }
    
    /// <summary>Whether this URL is currently valid. False if manually invalidated (e.g., album deleted)</summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>Navigation property to Photo</summary>
    public Photo Photo { get; set; } = null!;
}

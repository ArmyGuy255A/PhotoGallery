namespace PhotoGallery.Models;

/// <summary>
/// Represents a specific quality version of a photo file in blob storage
/// Supports Thumbnail, Low, Medium, High, Original, and Raw quality levels
/// </summary>
public class PhotoFile
{
    public Guid Id { get; set; }
    
    public Guid PhotoId { get; set; }
    
    /// <summary>
    /// Quality level of this file: Thumbnail, Low, Medium, High, Original, or Raw
    /// </summary>
    public PhotoFileQuality Quality { get; set; }
    
    /// <summary>
    /// Size of the file in bytes
    /// </summary>
    public long FileSize { get; set; }
    
    /// <summary>
    /// Path in blob storage where this file is stored
    /// Example: photos/{albumId}/{photoId}/thumbnail.jpg
    /// </summary>
    public string BlobPath { get; set; } = string.Empty;
    
    /// <summary>
    /// When this file was created/processed
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    public virtual Photo? Photo { get; set; }
}

/// <summary>
/// Enumeration of supported photo quality levels
/// </summary>
public enum PhotoFileQuality
{
    Thumbnail = 0,  // 200x200 px
    Low = 1,        // 800x800 px
    Medium = 2,     // 1920x1920 px
    High = 3,       // 3840x3840 px
    Original = 4,   // Original resolution
    Raw = 5         // Raw file (e.g., .CR2, .NEF)
}

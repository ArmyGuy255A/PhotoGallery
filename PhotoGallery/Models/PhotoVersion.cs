namespace PhotoGallery.Models;

public enum PhotoQuality
{
    Raw = 0,
    LowCompression = 1,
    MediumCompression = 2,
    HighCompression = 3
}

public class PhotoVersion
{
    public Guid Id { get; set; }
    public Guid PhotoId { get; set; }
    public PhotoQuality Quality { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime? ProcessedDate { get; set; }
    
    // Navigation property
    public virtual Photo? Photo { get; set; }
}

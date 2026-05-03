namespace PhotoGallery.Models;

public enum PhotoProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Complete = 2,
    Failed = 3
}

public class Photo
{
    public Guid Id { get; set; }
    public Guid AlbumId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public string? Metadata { get; set; }
    
    // Processing status tracking
    public PhotoProcessingStatus ProcessingStatus { get; set; } = PhotoProcessingStatus.Pending;
    public bool HasThumbnail { get; set; }
    public bool HasLow { get; set; }
    public bool HasMedium { get; set; }
    public bool HasHigh { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessingCompletedAt { get; set; }
    
    // Legacy field (still used, kept for compatibility)
    public bool ProcessingComplete { get; set; }
    
    public byte[] RowVersion { get; set; } = new byte[] { 1 };
    
    // Navigation properties
    public virtual Album? Album { get; set; }
    public virtual ICollection<PhotoVersion> PhotoVersions { get; set; } = new List<PhotoVersion>();
    public virtual ICollection<PhotoFile> PhotoFiles { get; set; } = new List<PhotoFile>();
    public virtual ICollection<PhotoVersionUrl> PhotoVersionUrls { get; set; } = new List<PhotoVersionUrl>();
}

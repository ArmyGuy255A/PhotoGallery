namespace PhotoGallery.Models;

public enum PhotoProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Complete = 2,
    Failed = 3,
    // Phase 2 — direct-to-blob upload.
    //
    // A Photo row is inserted in Uploading state when /upload-tickets mints
    // a write SAS, then transitions to Pending in /upload-complete once the
    // SPA confirms the PUT landed. Rows in Uploading are invisible to album
    // listings (PhotoRepository.GetAlbumPhotosAsync filters them out) so the
    // user never sees a ghost row while the browser is mid-PUT.
    //
    // Value 4 is chosen to sit after the existing 0..3 contiguous block so
    // no existing persisted state shifts. The column is mapped as int by
    // PhotoConfiguration so no migration is required.
    Uploading = 4
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

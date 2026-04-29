namespace PhotoGallery.Models;

public class Photo
{
    public Guid Id { get; set; }
    public Guid AlbumId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public string? Metadata { get; set; }
    public bool ProcessingComplete { get; set; }
    public byte[] RowVersion { get; set; } = new byte[] { 1 };
    
    // Navigation properties
    public virtual Album? Album { get; set; }
    public virtual ICollection<PhotoVersion> PhotoVersions { get; set; } = new List<PhotoVersion>();
}

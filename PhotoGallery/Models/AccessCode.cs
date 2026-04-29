namespace PhotoGallery.Models;

public class AccessCode
{
    public Guid Id { get; set; }
    public Guid AlbumId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ExpirationDate { get; set; }
    public byte[] RowVersion { get; set; } = new byte[] { 1 };
    
    // Navigation properties
    public virtual Album? Album { get; set; }
    public virtual ICollection<UserAccessLog> UserAccessLogs { get; set; } = new List<UserAccessLog>();
}

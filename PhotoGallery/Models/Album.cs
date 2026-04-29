namespace PhotoGallery.Models;

public class Album
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public byte[] RowVersion { get; set; } = new byte[] { 1 };
    
    // Navigation properties
    public virtual User? Owner { get; set; }
    public virtual ICollection<Photo> Photos { get; set; } = new List<Photo>();
    public virtual ICollection<AccessCode> AccessCodes { get; set; } = new List<AccessCode>();
}

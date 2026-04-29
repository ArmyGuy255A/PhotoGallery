namespace PhotoGallery.Models;

public class UserAccessLog
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }
    public Guid AccessCodeId { get; set; }
    public DateTime AccessDate { get; set; }
    public string? IpAddress { get; set; }
    
    // Navigation properties
    public virtual User? User { get; set; }
    public virtual AccessCode? AccessCode { get; set; }
}

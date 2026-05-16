namespace PhotoGallery.Models;

public class UserAccessLog
{
    public Guid Id { get; set; }
    public string? UserId { get; set; }
    public Guid AccessCodeId { get; set; }
    public DateTime AccessDate { get; set; }
    public string? IpAddress { get; set; }

    /// <summary>
    /// Truncated User-Agent string from the browser that entered the code.
    /// Used by the Admin access-code analytics view to distinguish unique
    /// browsers / devices behind a single IP. Capped at 256 chars.
    /// </summary>
    public string? UserAgent { get; set; }

    // Navigation properties
    public virtual User? User { get; set; }
    public virtual AccessCode? AccessCode { get; set; }
}

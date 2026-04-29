using Microsoft.AspNetCore.Identity;

namespace PhotoGallery.Models;

public class User : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedDate { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public virtual ICollection<Album> Albums { get; set; } = new List<Album>();
    public virtual ICollection<Photo> Photos { get; set; } = new List<Photo>();
    public virtual ICollection<AccessCode> AccessCodes { get; set; } = new List<AccessCode>();
    public virtual ICollection<UserAccessLog> UserAccessLogs { get; set; } = new List<UserAccessLog>();
}

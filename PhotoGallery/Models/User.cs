using Microsoft.AspNetCore.Identity;

namespace PhotoGallery.Models;

public class User : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedDate { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Last time the user authenticated. Stamped by
    /// <c>ExternalAuthService.HandleExternalLoginAsync</c> on every successful
    /// Google OAuth callback. Drives the Admin page's user table.
    /// Nullable so legacy rows that pre-date the column read as "never".
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    // Navigation properties
    public virtual ICollection<Album> Albums { get; set; } = new List<Album>();
    public virtual ICollection<Photo> Photos { get; set; } = new List<Photo>();
    public virtual ICollection<AccessCode> AccessCodes { get; set; } = new List<AccessCode>();
    public virtual ICollection<UserAccessLog> UserAccessLogs { get; set; } = new List<UserAccessLog>();
}

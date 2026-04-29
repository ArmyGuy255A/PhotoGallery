using PhotoGallery.Enums;

namespace PhotoGallery.Classes;

public class ExternalUserInfo
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Picture { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string GivenName { get; set; } = string.Empty;
    public ExternalAuthProvider Provider { get; set; }
    public string Error { get; set; } = string.Empty;
}
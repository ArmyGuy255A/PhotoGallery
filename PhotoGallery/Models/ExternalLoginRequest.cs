namespace PhotoGallery.Models;

public class ExternalLoginRequest
{
    public string Provider { get; set; } = string.Empty;
    public string IdToken { get; set; } = string.Empty;
}
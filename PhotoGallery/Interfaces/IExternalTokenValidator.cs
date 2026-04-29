using PhotoGallery.Classes;

namespace PhotoGallery.Interfaces;

public interface IExternalTokenValidator
{
    Task<ExternalUserInfo> ValidateTokenAsync(string token);
}
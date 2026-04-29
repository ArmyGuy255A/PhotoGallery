using PhotoGallery.Models;

namespace PhotoGallery.Interfaces;

public interface IExternalAuthService
{
    Task<string?> HandleExternalLoginAsync(string provider, string idToken);

    Task<bool?> HandleExternalSignInAsync(User user);
}
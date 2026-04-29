using PhotoGallery.Interfaces;

namespace PhotoGallery.Classes;

public class TokenValidatorFactory
{
    public static IExternalTokenValidator CreateValidator(string provider)
    {
        if (string.IsNullOrEmpty(provider))
        {
            throw new ArgumentNullException(nameof(provider), "Provider cannot be null or empty");
        }
        if (!Enum.TryParse(provider, out Enums.ExternalAuthProvider authProvider))
        {
            throw new ArgumentException($"Invalid provider: {provider}", nameof(provider));
        }
        if (!Enum.IsDefined(typeof(Enums.ExternalAuthProvider), authProvider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider), $"Provider {provider} is not supported");
        }
        
        return authProvider switch
        {
            Enums.ExternalAuthProvider.Google => new GoogleTokenValidator(),
            // Enums.ExternalAuthProvider.Facebook => new FacebookTokenValidator(),
            // Enums.ExternalAuthProvider.Microsoft => new MicrosoftTokenValidator(),
            _ => throw new NotImplementedException($"No validator implemented for {provider}")
        };
    }
}
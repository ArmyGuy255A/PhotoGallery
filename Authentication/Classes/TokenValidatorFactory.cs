using Authentication.Enums;
using Authentication.Interfaces;

namespace Authentication.Classes;

public class TokenValidatorFactory
{
    public static IExternalTokenValidator CreateValidator(string provider)
    {
        if (string.IsNullOrEmpty(provider))
        {
            throw new ArgumentNullException(nameof(provider), "Provider cannot be null or empty");
        }
        if (!Enum.TryParse(provider, ignoreCase: true, out ExternalAuthProvider authProvider))
        {
            throw new ArgumentException($"Invalid provider: {provider}", nameof(provider));
        }
        if (!Enum.IsDefined(typeof(ExternalAuthProvider), authProvider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider), $"Provider {provider} is not supported");
        }

        return authProvider switch
        {
            ExternalAuthProvider.Google => new GoogleTokenValidator(),
            // ExternalAuthProvider.Facebook => new FacebookTokenValidator(),
            // ExternalAuthProvider.Microsoft => new MicrosoftTokenValidator(),
            _ => throw new NotImplementedException($"No validator implemented for {provider}")
        };
    }
}
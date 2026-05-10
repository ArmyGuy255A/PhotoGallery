using Authentication.Enums;
using Authentication.Interfaces;
using Google.Apis.Auth;

namespace Authentication.Classes;

public class GoogleTokenValidator : IExternalTokenValidator
{
    /// <summary>
    /// Tolerance window allowed between Google's token-issuance clock and the
    /// validating server's clock. Without this, even a few seconds of skew
    /// surfaces as <c>JWT is not yet valid</c>. Five minutes mirrors common
    /// OIDC validator defaults (Microsoft.IdentityModel uses 5 min, AWS Cognito
    /// uses 5 min) and is well within Google's own published guidance.
    /// </summary>
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(5);

    public async Task<ExternalUserInfo> ValidateTokenAsync(string token)
    {
        ExternalUserInfo userInfo = new ExternalUserInfo
        {
            Provider = ExternalAuthProvider.Google
        };

        GoogleJsonWebSignature.Payload payload;
        try
        {
            // ValidationSettings.IssuedAtClockTolerance gives the Google validator
            // a clock-skew window for both iat (issued-at) and nbf (not-before)
            // checks; ExpirationTimeClockTolerance covers exp.
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                IssuedAtClockTolerance = ClockSkewTolerance,
                ExpirationTimeClockTolerance = ClockSkewTolerance,
            };
            payload = await GoogleJsonWebSignature.ValidateAsync(token, settings);
        }
        catch (Exception ex)
        {
            userInfo.Error = ex.Message;
            return userInfo;
        }

        if (payload == null)
        {
            userInfo.Error = "Invalid token";
            return userInfo;
        }

        if (string.IsNullOrEmpty(payload.Email))
        {
            userInfo.Error = "Invalid email";
        }

        if (string.IsNullOrEmpty(payload.Name))
        {
            userInfo.Error = "Invalid name";
        }

        userInfo.Email = payload.Email;
        userInfo.Name = payload.Name;
        userInfo.Picture = payload.Picture;
        userInfo.FamilyName = payload.FamilyName;
        userInfo.GivenName = payload.GivenName;

        return userInfo;
    }
}
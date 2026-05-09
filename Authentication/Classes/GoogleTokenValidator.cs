using Authentication.Enums;
using Authentication.Interfaces;
using Google.Apis.Auth;

namespace Authentication.Classes;

public class GoogleTokenValidator : IExternalTokenValidator
{
    public async Task<ExternalUserInfo> ValidateTokenAsync(string token)
    {
        ExternalUserInfo userInfo = new ExternalUserInfo
        {
            Provider = ExternalAuthProvider.Google
        };

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(token);
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
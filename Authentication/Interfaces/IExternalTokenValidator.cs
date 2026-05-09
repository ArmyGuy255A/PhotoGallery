using Authentication.Classes;

namespace Authentication.Interfaces;

public interface IExternalTokenValidator
{
    Task<ExternalUserInfo> ValidateTokenAsync(string token);
}
using System.Text;
using System.Text.Json;
using Authentication.Classes;

namespace Authentication.Helpers;

/// <summary>
/// JWT helper utilities that do NOT validate signatures. Use these only to
/// peek at non-trust-bearing fields (issuer, audience, expiry) before routing
/// to the appropriate validator. Always validate via
/// <see cref="Authentication.Interfaces.IExternalTokenValidator"/> before
/// trusting any decoded value.
/// </summary>
public static class JwtHelper
{
    /// <summary>
    /// Decode the payload portion of a JWS (header.payload.signature) and return
    /// a typed view of the well-known IdP claims. Returns null if the token is
    /// malformed.
    /// </summary>
    public static IdpTokenInfo? DecodeIdToken(string idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payloadBytes = DecodeBase64Url(parts[1]);
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            return JsonSerializer.Deserialize<IdpTokenInfo>(payloadJson);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        var pad = output.Length % 4;
        if (pad > 0)
        {
            output = output + new string('=', 4 - pad);
        }
        return Convert.FromBase64String(output);
    }
}

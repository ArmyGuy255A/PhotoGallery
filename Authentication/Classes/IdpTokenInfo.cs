namespace Authentication.Classes;

/// <summary>
/// Decoded fields from a third-party identity-provider id_token (the un-validated
/// payload). Used by callers that want to know the issuer before picking a validator.
///
/// Always re-validate via <see cref="GoogleTokenValidator"/> (or the appropriate impl)
/// before trusting any of these fields — base64-decoding alone proves nothing.
/// </summary>
public class IdpTokenInfo
{
    /// <summary>Issuer claim — e.g. <c>https://accounts.google.com</c></summary>
    public string iss { get; set; } = string.Empty;

    /// <summary>Subject claim — provider's stable user identifier</summary>
    public string sub { get; set; } = string.Empty;

    /// <summary>Audience — should match our configured client_id</summary>
    public string aud { get; set; } = string.Empty;

    /// <summary>Email claim if present</summary>
    public string email { get; set; } = string.Empty;

    /// <summary>Token expiry as Unix epoch seconds</summary>
    public long exp { get; set; }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Authentication.Services;

/// <summary>
/// Issues HS256 JWTs signed with the configured shared secret.
///
/// This service is intentionally decoupled from any concrete user entity —
/// callers in the web app resolve the user (UserManager) and supply the
/// pre-built claim set or basic identity fields. That keeps the
/// <c>Authentication</c> project free of any domain-layer dependencies.
///
/// Reference: photogallery-architect-skill — "Cross-Cutting Concerns"
/// Reference: photogallery-auth-skill — JWT issuance flow
/// </summary>
public class JwtTokenService
{
    private readonly ConfigurationSettings _settings;

    public JwtTokenService(IOptions<ConfigurationSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <summary>
    /// Issue a JWT signed with the configured key. Caller supplies the full claim set.
    /// </summary>
    public string GenerateToken(IList<Claim> claims)
    {
        var jwtConfig = _settings.Authentication.Jwt;

        if (string.IsNullOrEmpty(jwtConfig.Key))
        {
            throw new InvalidOperationException(
                "Authentication:Jwt:Key is not configured. Cannot issue tokens.");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: jwtConfig.Issuer,
            audience: jwtConfig.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(jwtConfig.ExpirationMinutes <= 0 ? 60 : jwtConfig.ExpirationMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    /// <summary>
    /// Issue a JWT for a user given pre-resolved identity fields and roles.
    /// The web-app caller is responsible for resolving roles via UserManager
    /// (which lives in the web project, not here, to keep the cross-cutting
    /// boundary clean).
    /// </summary>
    public string GenerateTokenForUser(string userId, string email, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Email, email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, userId),
        };

        foreach (var role in roles)
        {
            // Emit the short-form "role" claim instead of the long
            // ClaimTypes.Role URI (http://schemas.microsoft.com/ws/2008/06/identity/claims/role).
            // ASP.NET's JwtBearer middleware (with default MapInboundClaims=true)
            // still maps this back to ClaimTypes.Role server-side, so
            // [Authorize(Roles="...")] keeps working — but the on-the-wire
            // JWT body is now the compact {"role":"Admin"} form that FE code
            // and external token consumers expect.
            claims.Add(new Claim("role", role));
        }

        return GenerateToken(claims);
    }
}

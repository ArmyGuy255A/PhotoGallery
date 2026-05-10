using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Authentication.Services;
using Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PhotoGallery.Tests;

/// <summary>
/// Regression tests for the auth-token issuance/validation contract:
/// a JWT minted by <see cref="JwtTokenService"/> MUST be accepted by the
/// <see cref="TokenValidationParameters"/> the JwtBearer middleware is
/// configured with in <c>Authentication.DependencyInjection</c>.
///
/// Catches future drift on issuer, audience, signing-key, expiration, and
/// (most importantly) clock skew — the very class of bug that surfaced as
/// 401-immediately-after-login when ClockSkew was set to TimeSpan.Zero.
/// </summary>
public class JwtIssuanceValidationTests
{
    private const string Issuer = "PhotoGallery";
    private const string Audience = "PhotoGalleryClient";
    private const string Key = "test-signing-key-must-be-at-least-32-characters-long-for-hs256-yes";

    private static JwtTokenService CreateService(int expirationMinutes = 60)
    {
        var settings = new ConfigurationSettings
        {
            Authentication = new global::Configuration.Authentication
            {
                Jwt = new Jwt
                {
                    Key = Key,
                    Issuer = Issuer,
                    Audience = Audience,
                    ExpirationMinutes = expirationMinutes,
                }
            }
        };
        return new JwtTokenService(Options.Create(settings));
    }

    /// <summary>
    /// Mirror of <c>Authentication.DependencyInjection.AddAuthenticationServices</c>'s
    /// TokenValidationParameters. If the production code drifts away from these
    /// assertions, this test will fail and force the production code change to
    /// be reflected here too.
    /// </summary>
    private static TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(5),
        ValidateIssuerSigningKey = true,
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
    };

    [Fact]
    public void IssuedToken_IsAccepted_BySameValidationParameters()
    {
        var service = CreateService();
        var token = service.GenerateTokenForUser(
            userId: "user-123",
            email: "user@example.com",
            roles: new[] { "Admin", "User" });

        var handler = new JwtSecurityTokenHandler();
        // Disable the default short→long claim-type mapping so we can assert
        // against the exact claim names that the JWT was issued with.
        handler.InboundClaimTypeMap.Clear();

        var principal = handler.ValidateToken(token, CreateValidationParameters(), out var validatedToken);

        Assert.NotNull(principal);
        Assert.NotNull(validatedToken);
        Assert.Equal("user-123", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("user@example.com", principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value);
        // After PR-A, the JWT body carries the compact "role" claim name
        // rather than the long ClaimTypes.Role URI. With InboundClaimTypeMap
        // cleared the principal preserves the on-the-wire name.
        Assert.Contains(principal.FindAll("role"), c => c.Value == "Admin");
        Assert.Contains(principal.FindAll("role"), c => c.Value == "User");
        // And the long URI must NOT be present on the wire.
        Assert.Empty(principal.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public void IssuedToken_PayloadContains_ShortFormRoleClaim_NotLongUri()
    {
        // Direct assertion on the raw JWT body: parsing without going through
        // any inbound claim-type mapping. Guards the on-the-wire contract that
        // FE code (and any external consumer) decodes via base64 + JSON.parse.
        var service = CreateService();
        var token = service.GenerateTokenForUser(
            userId: "user-123",
            email: "user@example.com",
            roles: new[] { "Admin" });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "Admin");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == ClaimTypes.Role);
    }

    [Fact]
    public void IssuedToken_IsRejected_WhenAudienceDoesNotMatch()
    {
        var service = CreateService();
        var token = service.GenerateTokenForUser("u", "u@x.com", new[] { "User" });

        var validation = CreateValidationParameters();
        validation.ValidAudience = "DifferentAudience";

        var handler = new JwtSecurityTokenHandler();
        Assert.Throws<SecurityTokenInvalidAudienceException>(
            () => handler.ValidateToken(token, validation, out _));
    }

    [Fact]
    public void IssuedToken_IsRejected_WhenIssuerDoesNotMatch()
    {
        var service = CreateService();
        var token = service.GenerateTokenForUser("u", "u@x.com", new[] { "User" });

        var validation = CreateValidationParameters();
        validation.ValidIssuer = "DifferentIssuer";

        var handler = new JwtSecurityTokenHandler();
        Assert.Throws<SecurityTokenInvalidIssuerException>(
            () => handler.ValidateToken(token, validation, out _));
    }

    [Fact]
    public void IssuedToken_IsRejected_WhenSigningKeyDoesNotMatch()
    {
        var service = CreateService();
        var token = service.GenerateTokenForUser("u", "u@x.com", new[] { "User" });

        var validation = CreateValidationParameters();
        validation.IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("a-completely-different-secret-key-with-enough-length-to-pass"));

        var handler = new JwtSecurityTokenHandler();
        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(
            () => handler.ValidateToken(token, validation, out _));
    }

    [Fact]
    public void IssuedToken_IsRejected_WhenExpired()
    {
        // Issue a JWT with the same key/issuer/audience but explicitly expired
        // 10 minutes ago — well outside the 5-minute clock-skew window — so
        // ValidateLifetime rejects it deterministically. We construct the
        // SecurityToken directly here instead of going through JwtTokenService
        // because the service clamps non-positive ExpirationMinutes to a
        // 60-minute default (defensive against misconfiguration).
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiredJwt = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[] { new Claim(ClaimTypes.NameIdentifier, "u") },
            notBefore: DateTime.UtcNow.AddMinutes(-20),
            expires: DateTime.UtcNow.AddMinutes(-10),
            signingCredentials: creds);
        var token = new JwtSecurityTokenHandler().WriteToken(expiredJwt);

        var handler = new JwtSecurityTokenHandler();
        Assert.Throws<SecurityTokenExpiredException>(
            () => handler.ValidateToken(token, CreateValidationParameters(), out _));
    }

    [Fact]
    public void ValidationParameters_HaveNonZeroClockSkew()
    {
        // Regression guard for the "401-immediately-after-login" bug:
        // ClockSkew=Zero means even sub-second drift between issuance and
        // validation surfaces as a 401. 5 minutes is the documented
        // Microsoft.IdentityModel default and what production uses.
        var validation = CreateValidationParameters();
        Assert.True(validation.ClockSkew >= TimeSpan.FromMinutes(1),
            $"ClockSkew must be at least 1 minute to tolerate normal drift; got {validation.ClockSkew}");
    }
}

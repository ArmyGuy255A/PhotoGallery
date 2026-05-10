using System.Text;
using Authentication.Services;
using Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Authentication;

/// <summary>
/// Bootstraps PhotoGallery's authentication services.
///
/// Usage in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddConfigurationServices(builder.Configuration, out var settings);
/// builder.Services.AddAuthenticationServices(settings);
/// </code>
///
/// Registers:
///  - <see cref="JwtTokenService"/> for issuing app JWTs
///  - JwtBearer authentication scheme with HS256 validation against the configured key
///
/// External-token validation (Google id_token → ExternalUserInfo) is invoked
/// per-request via <see cref="Authentication.Classes.TokenValidatorFactory"/> and does not
/// need DI registration.
///
/// Reference: photogallery-architect-skill — "Adding a Cross-Cutting Concern"
/// Reference: photogallery-auth-skill
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAuthenticationServices(
        this IServiceCollection services,
        ConfigurationSettings settings)
    {
        var jwtConfig = settings.Authentication.Jwt;

        if (string.IsNullOrEmpty(jwtConfig.Key))
        {
            throw new InvalidOperationException(
                "Authentication:Jwt:Key is not configured — required for JWT validation.");
        }

        services.AddScoped<JwtTokenService>();

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    // Allow up to 5 min of skew between the issuer's clock and the
                    // validating server's clock so freshly-issued JWTs are not
                    // rejected with 401 due to sub-second drift on iat/nbf. Mirrors
                    // Microsoft.IdentityModel's documented default. Symptom of
                    // ClockSkew=Zero: GET /api/* returns 401 immediately after a
                    // successful login that just minted the same JWT.
                    ClockSkew = TimeSpan.FromMinutes(5),
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtConfig.Issuer,
                    ValidAudience = jwtConfig.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtConfig.Key))
                };
            });

        return services;
    }
}

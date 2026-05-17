using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PhotoGallery.Models;

namespace PhotoGallery.Middleware;

public class DisableAuthMiddleware
{
    private readonly RequestDelegate _next;

    public DisableAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, UserManager<User> userManager, ILogger<DisableAuthMiddleware> logger, IConfiguration config)
    {
        // Check environment variable first, then fall back to configuration
        var disableAuth = Environment.GetEnvironmentVariable("DISABLE_AUTH") 
            ?? config["DISABLE_AUTH"]
            ?? "false";
        
        logger.LogTrace("DisableAuthMiddleware: DISABLE_AUTH = '{DisableAuth}'", disableAuth);
        
        if (disableAuth.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            const string testEmail = "testadmin@localhost";
            logger.LogInformation("DISABLE_AUTH is TRUE - attempting to authenticate as test user: {Email}", testEmail);
            
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                var testUser = await userManager.FindByEmailAsync(testEmail);
                
                if (testUser != null)
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, testUser.Id),
                        new Claim(ClaimTypes.Email, testUser.Email ?? string.Empty),
                        new Claim(ClaimTypes.Name, testUser.UserName ?? string.Empty),
                    };
                    
                    var roles = await userManager.GetRolesAsync(testUser);
                    foreach (var role in roles)
                    {
                        claims.Add(new Claim(ClaimTypes.Role, role));
                    }
                    
                    var identity = new ClaimsIdentity(claims, "DisableAuth");
                    context.User = new ClaimsPrincipal(identity);
                    
                    logger.LogInformation("DISABLE_AUTH activated: User authenticated as {Email} with roles: {Roles}", testEmail, string.Join(",", roles));
                }
                else
                {
                    logger.LogWarning("DISABLE_AUTH enabled but test user {Email} not found", testEmail);
                }
            }
        }
        else
        {
            logger.LogDebug("DISABLE_AUTH is not 'true' (value: '{DisableAuth}')", disableAuth);
        }
        
        await _next(context);
    }
}

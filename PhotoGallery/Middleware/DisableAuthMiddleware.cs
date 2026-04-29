using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PhotoGallery.Models;

namespace PhotoGallery.Middleware;

public class DisableAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DisableAuthMiddleware> _logger;

    public DisableAuthMiddleware(RequestDelegate next, ILogger<DisableAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, UserManager<User> userManager)
    {
        var disableAuth = Environment.GetEnvironmentVariable("DISABLE_AUTH");
        
        if (disableAuth?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            const string testEmail = "testadmin@localhost";
            
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
                    
                    _logger.LogInformation("DISABLE_AUTH activated: User authenticated as {Email}", testEmail);
                }
                else
                {
                    _logger.LogWarning("DISABLE_AUTH enabled but test user {Email} not found", testEmail);
                }
            }
        }
        
        await _next(context);
    }
}

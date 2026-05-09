using Authentication.Classes;
using Authentication.Interfaces;
using Authentication.Services;
using Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Services;

/// <summary>
/// Web-app glue between the cross-cutting <c>Authentication</c> validators
/// and PhotoGallery's domain User entity. Lives in PhotoGallery (not the
/// Authentication sub-project) because it depends on UserManager&lt;User&gt;
/// and SignInManager&lt;User&gt;, both of which need the concrete domain type.
/// </summary>
public class ExternalAuthService : IExternalAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly JwtTokenService _tokenService;
    private readonly ConfigurationSettings _settings;
    private readonly ILogger<ExternalAuthService> _logger;

    public ExternalAuthService(
        UserManager<User> userManager,
        JwtTokenService tokenService,
        IOptions<ConfigurationSettings> settings,
        SignInManager<User> signInManager,
        ILogger<ExternalAuthService> logger)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _settings = settings.Value;
        _signInManager = signInManager;
        _logger = logger;
    }

    public async Task<string?> HandleExternalLoginAsync(string provider, string idToken)
    {
        _logger.LogInformation("HandleExternalLogin: validating {Provider} idToken", provider);
        IExternalTokenValidator validator = TokenValidatorFactory.CreateValidator(provider);
        ExternalUserInfo userInfo = await validator.ValidateTokenAsync(idToken);

        if (!string.IsNullOrEmpty(userInfo.Error))
        {
            _logger.LogWarning("HandleExternalLogin: token validation failed for {Provider}: {Error}", provider, userInfo.Error);
            return null;
        }
        _logger.LogInformation("HandleExternalLogin: token validated for {Email} (name='{Name}')", userInfo.Email, userInfo.Name);

        var user = await _userManager.FindByEmailAsync(userInfo.Email);
        if (user == null)
        {
            _logger.LogInformation("HandleExternalLogin: no existing user for {Email} — creating new account", userInfo.Email);
            user = new User
            {
                UserName = userInfo.Email,
                Email = userInfo.Email,
                EmailConfirmed = true,
                CreatedDate = DateTime.UtcNow,
                IsActive = true
            };

            var result = await _userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                _logger.LogError("HandleExternalLogin: UserManager.CreateAsync failed for {Email}: {Errors}",
                    userInfo.Email, string.Join("; ", result.Errors.Select(e => $"{e.Code}={e.Description}")));
                return null;
            }
            _logger.LogInformation("HandleExternalLogin: created User {UserId} for {Email}", user.Id, user.Email);

            // Determine role based on configured admin email
            var adminEmail = string.IsNullOrEmpty(_settings.Auth.AdminEmail)
                ? "mrdieppa@gmail.com"
                : _settings.Auth.AdminEmail;
            var role = string.Equals(user.Email, adminEmail, StringComparison.OrdinalIgnoreCase) ? "Admin" : "User";
            await _userManager.AddToRoleAsync(user, role);
            _logger.LogInformation("HandleExternalLogin: assigned role {Role} to {Email} (admin email='{AdminEmail}')", role, user.Email, adminEmail);
        }
        else
        {
            _logger.LogInformation("HandleExternalLogin: existing user {UserId} found for {Email}", user.Id, user.Email);
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            _logger.LogError("HandleExternalLogin: UserManager.UpdateAsync failed for {Email}: {Errors}",
                user.Email, string.Join("; ", updateResult.Errors.Select(e => $"{e.Code}={e.Description}")));
            return null;
        }

        // Resolve roles in PhotoGallery (web layer) and pass to the
        // cross-cutting JwtTokenService for issuance — keeps Authentication
        // free of any domain-User dependency.
        var roles = await _userManager.GetRolesAsync(user);
        var token = _tokenService.GenerateTokenForUser(user.Id, user.Email ?? string.Empty, roles);
        _logger.LogInformation("HandleExternalLogin: issued JWT for {Email} with roles [{Roles}]", user.Email, string.Join(", ", roles));

        // Sign in the user (cookie scheme — used by legacy server-side OAuth flow)
        await HandleExternalSignInAsync(user);

        return token;
    }

    public async Task<bool?> HandleExternalSignInAsync(User user)
    {
        await _signInManager.SignInAsync(user, false);
        return await _signInManager.CanSignInAsync(user);
    }
}


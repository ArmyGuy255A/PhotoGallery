using Authentication.Classes;
using Authentication.Interfaces;
using Authentication.Services;
using Configuration;
using Microsoft.AspNetCore.Identity;
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

    public ExternalAuthService(
        UserManager<User> userManager,
        JwtTokenService tokenService,
        IOptions<ConfigurationSettings> settings,
        SignInManager<User> signInManager)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _settings = settings.Value;
        _signInManager = signInManager;
    }

    public async Task<string?> HandleExternalLoginAsync(string provider, string idToken)
    {
        IExternalTokenValidator validator = TokenValidatorFactory.CreateValidator(provider);
        ExternalUserInfo userInfo = await validator.ValidateTokenAsync(idToken);

        if (!string.IsNullOrEmpty(userInfo.Error))
            return null;

        var user = await _userManager.FindByEmailAsync(userInfo.Email);
        if (user == null)
        {
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
                return null;

            // Determine role based on configured admin email
            var adminEmail = string.IsNullOrEmpty(_settings.Auth.AdminEmail)
                ? "mrdieppa@gmail.com"
                : _settings.Auth.AdminEmail;
            if (string.Equals(user.Email, adminEmail, StringComparison.OrdinalIgnoreCase))
            {
                await _userManager.AddToRoleAsync(user, "Admin");
            }
            else
            {
                await _userManager.AddToRoleAsync(user, "User");
            }
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return null;

        // Resolve roles in PhotoGallery (web layer) and pass to the
        // cross-cutting JwtTokenService for issuance — keeps Authentication
        // free of any domain-User dependency.
        var roles = await _userManager.GetRolesAsync(user);
        var token = _tokenService.GenerateTokenForUser(user.Id, user.Email ?? string.Empty, roles);

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


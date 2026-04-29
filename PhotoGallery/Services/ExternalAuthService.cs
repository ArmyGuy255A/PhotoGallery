using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PhotoGallery.Classes;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Services;

public class ExternalAuthService : IExternalAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly JwtTokenService _tokenService;
    private readonly IConfiguration _configuration;

    public ExternalAuthService(UserManager<User> userManager, JwtTokenService tokenService, IConfiguration configuration, SignInManager<User> signInManager)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _configuration = configuration;
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

            // Determine role based on email
            var adminEmail = _configuration["Auth:AdminEmail"] ?? "mrdieppa@gmail.com";
            if (user.Email == adminEmail)
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

        var token = await _tokenService.GenerateTokenAsync(user);
        
        // Sign in the user
        await HandleExternalSignInAsync(user);

        return token;
    }

    public async Task<bool?> HandleExternalSignInAsync(User user)
    {
        await _signInManager.SignInAsync(user, false);
        
        return await _signInManager.CanSignInAsync(user);
    }
}


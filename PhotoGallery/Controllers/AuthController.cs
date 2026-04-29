using System.Security.Claims;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PhotoGallery.Classes;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services;

namespace PhotoGallery.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly JwtTokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly IExternalAuthService _externalAuthService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<User> userManager,
        JwtTokenService tokenService,
        IConfiguration configuration,
        IExternalAuthService externalAuthService,
        SignInManager<User> signInManager,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _configuration = configuration;
        _externalAuthService = externalAuthService;
        _signInManager = signInManager;
        _logger = logger;
    }

    /// <summary>
    /// External login endpoint - handles OAuth token validation and user creation
    /// </summary>
    [HttpPost("external-login")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest request)
    {
        try
        {
            var token = await _externalAuthService.HandleExternalLoginAsync(request.Provider, request.IdToken);
            if (token == null)
            {
                _logger.LogWarning("Invalid external login for provider: {Provider}", request.Provider);
                return BadRequest(new { error = "Invalid external login." });
            }

            return Ok(new { token });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in external login");
            return StatusCode(500, new { error = "An error occurred during authentication" });
        }
    }

    /// <summary>
    /// Initiates Google OAuth login flow
    /// </summary>
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        var redirectUrl = Url.Action(nameof(GoogleCallback), "Auth", new { returnUrl });
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Google OAuth callback endpoint
    /// </summary>
    [HttpGet("google-callback")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleCallback(string? returnUrl = null)
    {
        try
        {
            var result = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);

            if (!result.Succeeded)
            {
                _logger.LogWarning("Google authentication failed");
                return BadRequest("Google authentication failed");
            }

            var email = result.Principal?.FindFirst(ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(email))
            {
                _logger.LogWarning("No email claim in Google response");
                return BadRequest("No email from Google");
            }

            // Use ExternalAuthService to create/update user and get JWT
            var idToken = result.Properties?.GetTokenValue("id_token");
            if (string.IsNullOrEmpty(idToken))
            {
                _logger.LogWarning("No ID token in Google response");
                return BadRequest("No ID token from Google");
            }

            var jwtToken = await _externalAuthService.HandleExternalLoginAsync("google", idToken);
            if (string.IsNullOrEmpty(jwtToken))
            {
                _logger.LogWarning("Failed to generate JWT token for user: {Email}", email);
                return BadRequest("Failed to generate JWT token");
            }

            _logger.LogInformation("User successfully authenticated: {Email}", email);

            // Return token (can be via redirect with token in query or as JSON)
            return Ok(new { token = jwtToken, email });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Google callback");
            return StatusCode(500, "An error occurred during authentication");
        }
    }

    /// <summary>
    /// Sign out the user
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User logged out");
        return Ok(new { message = "Logged out successfully" });
    }

    /// <summary>
    /// Get current user info and roles
    /// </summary>
    [HttpGet("me")]
    [AllowAnonymous]  // Allow anonymous access so the frontend can call it for initialization
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(email))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        var roles = await _userManager.GetRolesAsync(user);
        
        // Generate a JWT token for this request
        var token = await _tokenService.GenerateTokenAsync(user);

        return Ok(new
        {
            accessToken = token,
            user = new
            {
                id = user.Id,
                email = user.Email,
                firstName = user.FirstName,
                lastName = user.LastName,
                roles = roles.ToList()
            }
        });
    }

    /// <summary>
    /// Refresh JWT token
    /// </summary>
    [HttpPost("refresh")]
    [Authorize]
    public async Task<IActionResult> RefreshToken()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        var token = await _tokenService.GenerateTokenAsync(user);
        return Ok(new { token });
    }
}

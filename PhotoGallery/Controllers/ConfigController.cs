using Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace PhotoGallery.Controllers;

/// <summary>
/// Exposes runtime, non-secret configuration to the SPA so values like the
/// Google OAuth ClientId can change via env var (Google__ClientId) without
/// rebuilding the Angular bundle. Single source of truth = backend
/// <see cref="ConfigurationSettings"/>.
///
/// IMPORTANT: only emit values that are safe to expose to anonymous browsers.
/// ClientSecret, JWT keys, connection strings, etc. MUST NOT be returned here.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly IOptionsMonitor<ConfigurationSettings> _settings;

    public ConfigController(IOptionsMonitor<ConfigurationSettings> settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Returns the public, browser-safe config the SPA needs at bootstrap.
    /// Anonymous so the login page can fetch it before any auth flow.
    /// </summary>
    [HttpGet("public")]
    [AllowAnonymous]
    public IActionResult GetPublic()
    {
        var s = _settings.CurrentValue;
        return Ok(new PublicConfigResponse
        {
            GoogleClientId = s.Google.ClientId
        });
    }
}

public class PublicConfigResponse
{
    public string GoogleClientId { get; set; } = string.Empty;
}

using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoGallery.Services;

namespace PhotoGallery.Controllers;

/// <summary>
/// GDPR endpoints scoped to the currently authenticated user (data portability + erasure).
/// Lives on a dedicated route ("/api/account/me") to avoid collision with any other
/// AccountController another slice may introduce.
///
/// Every endpoint resolves the userId from the JWT NameIdentifier claim — clients
/// cannot export or delete another user's data via path/query parameters.
/// </summary>
[Authorize]
[ApiController]
[Route("api/account/me")]
public class AccountGdprController : ControllerBase
{
    private readonly GdprService _gdpr;
    private readonly ILogger<AccountGdprController> _logger;

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AccountGdprController(GdprService gdpr, ILogger<AccountGdprController> logger)
    {
        _gdpr = gdpr;
        _logger = logger;
    }

    /// <summary>
    /// Right to data portability: returns a JSON file with all of the caller's data.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportMyData()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        UserDataExport export;
        try
        {
            export = await _gdpr.ExportUserDataAsync(userId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Export requested for missing user {UserId}", userId);
            return NotFound();
        }

        var json = JsonSerializer.Serialize(export, ExportJsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var fileName = $"photogallery-export-{DateTime.UtcNow:yyyyMMdd}.json";
        return File(bytes, "application/json", fileName);
    }

    /// <summary>
    /// Right to erasure: hard-deletes the caller's account and all owned data.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteMyAccount()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var actorEmail = User.FindFirst(ClaimTypes.Email)?.Value
                         ?? User.FindFirst("email")?.Value
                         ?? User.Identity?.Name
                         ?? "unknown";

        try
        {
            await _gdpr.DeleteUserAsync(userId, actorEmail);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Delete requested for missing user {UserId}", userId);
            return NotFound();
        }

        return NoContent();
    }
}

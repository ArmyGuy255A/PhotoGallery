using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Models;
using System.Security.Claims;

namespace PhotoGallery.Controllers;

/// <summary>
/// Authenticated user account endpoints. EPIC-02 Slice B: lets users save
/// access codes to their account so they can revisit shared albums from
/// /shared-albums without re-entering codes.
/// </summary>
[Authorize]
[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AccountController> _logger;

    public AccountController(ApplicationDbContext db, ILogger<AccountController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// List every access code the current user has saved, joined with
    /// album metadata for display.
    /// </summary>
    [HttpGet("access-codes")]
    public async Task<ActionResult<List<SavedAccessCodeDto>>> GetSavedAccessCodes()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var rows = await _db.SavedAccessCodes
            .Where(s => s.UserId == userId)
            .Include(s => s.AccessCode!)
                .ThenInclude(ac => ac.Album)
            .OrderByDescending(s => s.SavedAt)
            .ToListAsync();

        var result = rows
            .Where(s => s.AccessCode != null)
            .Select(ToDto)
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Save an access code to the current user's account. Idempotent: if the
    /// user has already saved this code, the existing row is returned.
    /// </summary>
    [HttpPost("access-codes")]
    public async Task<ActionResult<SavedAccessCodeDto>> SaveAccessCode([FromBody] SaveAccessCodeRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (request == null || string.IsNullOrWhiteSpace(request.Code))
            return BadRequest("Code is required.");

        var accessCode = await _db.AccessCodes
            .Include(ac => ac.Album)
            .FirstOrDefaultAsync(ac => ac.Code == request.Code);

        if (accessCode == null)
            return NotFound("Access code not found.");

        if (accessCode.ExpirationDate.HasValue && accessCode.ExpirationDate < DateTime.UtcNow)
            return BadRequest("Access code has expired.");

        var existing = await _db.SavedAccessCodes
            .Include(s => s.AccessCode!)
                .ThenInclude(ac => ac.Album)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.AccessCodeId == accessCode.Id);

        if (existing != null)
        {
            _logger.LogInformation("User {UserId} re-saved already-saved code {CodeId} (idempotent)",
                userId, accessCode.Id);
            return Ok(ToDto(existing));
        }

        var saved = new SavedAccessCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccessCodeId = accessCode.Id,
            SavedAt = DateTime.UtcNow,
            AccessCode = accessCode
        };

        _db.SavedAccessCodes.Add(saved);
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} saved access code {CodeId} (album {AlbumId})",
            userId, accessCode.Id, accessCode.AlbumId);

        return StatusCode(201, ToDto(saved));
    }

    /// <summary>
    /// Remove a saved-access-code link. Does not delete the underlying
    /// AccessCode. Enforces ownership: 403 if the saved row belongs to
    /// another user.
    /// </summary>
    [HttpDelete("access-codes/{savedId}")]
    public async Task<IActionResult> DeleteSavedAccessCode(Guid savedId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var saved = await _db.SavedAccessCodes.FirstOrDefaultAsync(s => s.Id == savedId);
        if (saved == null)
            return NotFound();

        if (saved.UserId != userId)
            return Forbid();

        _db.SavedAccessCodes.Remove(saved);
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} removed saved access code {SavedId}", userId, savedId);
        return NoContent();
    }

    private static SavedAccessCodeDto ToDto(SavedAccessCode s) => new()
    {
        Id = s.Id.ToString(),
        Code = s.AccessCode?.Code ?? string.Empty,
        AlbumId = s.AccessCode?.AlbumId.ToString() ?? string.Empty,
        AlbumTitle = s.AccessCode?.Album?.Title ?? string.Empty,
        SavedAt = s.SavedAt,
        ExpirationDate = s.AccessCode?.ExpirationDate
    };
}

public class SaveAccessCodeRequest
{
    public string Code { get; set; } = string.Empty;
}

public class SavedAccessCodeDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string AlbumId { get; set; } = string.Empty;
    public string AlbumTitle { get; set; } = string.Empty;
    public DateTime SavedAt { get; set; }
    public DateTime? ExpirationDate { get; set; }
}

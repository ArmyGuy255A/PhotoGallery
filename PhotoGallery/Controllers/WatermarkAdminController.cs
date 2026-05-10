using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoGallery.Services;

namespace PhotoGallery.Controllers;

/// <summary>
/// Admin endpoint to force regeneration of watermarked variants. Used as a one-time-run
/// sweep after merging the display-name fix so existing albums (whose watermarks were
/// painted with the raw <c>UploadedBy</c> GUID) self-heal back to "© First Last" on the
/// next public view.
///
/// Reference: TODO at <c>ImageProcessingService.cs:290</c>, PRs #47 / #48, and the
/// May-2026 EPIC issue.
///
/// Authorization: <c>Administrator</c> role only. <c>Reason</c> is required and audit-logged.
/// </summary>
[Authorize(Roles = "Administrator")]
[ApiController]
[Route("api/admin/watermark")]
public class WatermarkAdminController : ControllerBase
{
    private readonly IWatermarkBackfillService _backfill;

    public WatermarkAdminController(IWatermarkBackfillService backfill)
    {
        _backfill = backfill;
    }

    /// <summary>
    /// Body: <c>{ "albumId"?: Guid, "reason": "string (required)" }</c>.
    /// When <c>albumId</c> is omitted, sweeps every album.
    /// </summary>
    [HttpPost("regenerate")]
    public async Task<IActionResult> Regenerate([FromBody] WatermarkRegenerateRequestDto body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Reason))
        {
            return BadRequest(new { error = "reason is required" });
        }

        var actorUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        var actorEmail = User.FindFirst(ClaimTypes.Email)?.Value
                         ?? User.FindFirst("email")?.Value
                         ?? User.Identity?.Name
                         ?? "unknown";

        var result = await _backfill.RegenerateAsync(
            new WatermarkBackfillRequest(body.AlbumId, body.Reason),
            actorUserId,
            actorEmail,
            ct);

        return Ok(new
        {
            scope = body.AlbumId?.ToString() ?? "all",
            reason = body.Reason,
            photosScanned = result.PhotosScanned,
            blobsDeleted = result.BlobsDeleted,
            blobsMissing = result.BlobsMissing,
            errors = result.Errors,
        });
    }
}

/// <summary>
/// Wire shape for <c>POST /api/admin/watermark/regenerate</c>. <c>Reason</c> is required
/// and audit-logged so prod sweeps remain traceable.
/// </summary>
public record WatermarkRegenerateRequestDto(Guid? AlbumId, string Reason);

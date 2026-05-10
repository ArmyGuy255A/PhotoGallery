using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services;
using System.Security.Claims;

namespace PhotoGallery.Controllers;

/// <summary>
/// Per-user, database-backed shopping cart endpoints (EPIC May 2026 — bug #9).
///
/// Cart items can span multiple albums. Authorisation rules:
/// <list type="bullet">
///   <item>Owner of the source album → allowed.</item>
///   <item>Admin → allowed.</item>
///   <item>User has a saved, non-expired access code for the album → allowed.</item>
///   <item>Else → 403.</item>
/// </list>
///
/// At <c>POST /api/cart</c> the item is authorised at add-time. At
/// <c>POST /api/cart/download</c> every item is RE-AUTHORISED at request-time
/// (saved codes may have expired since the item was added). Items that have
/// lost authorisation are dropped from the ZIP and reported in the
/// <c>X-Skipped-Photo-Ids</c> response header. If every item is unauthorised
/// the endpoint returns 403.
/// </summary>
[Authorize]
[ApiController]
[Route("api/cart")]
public class CartController : ControllerBase
{
    /// <summary>Hard cap kept in lock-step with the FE constant <c>MAX_CART_SIZE</c>.</summary>
    public const int MaxCartItems = 100;

    /// <summary>TTL for short-lived cart-thumbnail URLs (matches the AccessCode flow).</summary>
    private const int ThumbnailUrlTtlMinutes = 15;

    private readonly IUserCartRepository _cartRepository;
    private readonly IPhotoRepository _photoRepository;
    private readonly IRepository<Album> _albumRepository;
    private readonly ApplicationDbContext _db;
    private readonly PhotoVersionUrlService _urlService;
    private readonly ICartZipService _cartZipService;
    private readonly ILogger<CartController> _logger;

    public CartController(
        IUserCartRepository cartRepository,
        IPhotoRepository photoRepository,
        IRepository<Album> albumRepository,
        ApplicationDbContext db,
        PhotoVersionUrlService urlService,
        ICartZipService cartZipService,
        ILogger<CartController> logger)
    {
        _cartRepository = cartRepository;
        _photoRepository = photoRepository;
        _albumRepository = albumRepository;
        _db = db;
        _urlService = urlService;
        _cartZipService = cartZipService;
        _logger = logger;
    }

    /// <summary>List the current user's cart.</summary>
    [HttpGet]
    public async Task<ActionResult<CartListResponse>> GetCart()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var rows = await _cartRepository.GetForUserAsync(userId);

        var items = new List<CartItemDto>(rows.Count);
        foreach (var row in rows)
        {
            string? thumbnailUrl = null;
            try
            {
                thumbnailUrl = await _urlService.GenerateShortLivedUrlAsync(
                    row.PhotoId, QualityType.Thumbnail, ThumbnailUrlTtlMinutes, watermarked: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to generate cart thumbnail URL for photo {PhotoId} (continuing)", row.PhotoId);
            }

            items.Add(new CartItemDto
            {
                PhotoId = row.PhotoId.ToString(),
                Quality = row.Quality.ToString(),
                SourceAlbumId = row.SourceAlbumId?.ToString(),
                SourceAlbumTitle = row.SourceAlbum?.Title,
                FileName = row.Photo?.FileName ?? string.Empty,
                ThumbnailUrl = thumbnailUrl,
                AddedAt = row.AddedAt
            });
        }

        return Ok(new CartListResponse { Items = items });
    }

    /// <summary>Add an item to the current user's cart.</summary>
    [HttpPost]
    public async Task<ActionResult<CartItemDto>> AddToCart([FromBody] AddCartItemRequest request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (request == null)
            return BadRequest("Request body is required.");

        if (!Guid.TryParse(request.PhotoId, out var photoId))
            return BadRequest("Invalid photoId.");

        if (!Enum.TryParse<QualityType>(request.Quality, ignoreCase: true, out var quality))
            return BadRequest("Invalid quality. Allowed: Low, Medium, High, Original.");

        if (quality == QualityType.Thumbnail)
            return BadRequest("Thumbnail is preview-only and cannot be added to cart.");

        var photo = await _photoRepository.GetByIdAsync(photoId);
        if (photo == null)
            return NotFound("Photo not found.");

        Guid? sourceAlbumId = null;
        if (!string.IsNullOrWhiteSpace(request.SourceAlbumId))
        {
            if (!Guid.TryParse(request.SourceAlbumId, out var parsed))
                return BadRequest("Invalid sourceAlbumId.");
            sourceAlbumId = parsed;
        }

        // Authorise against the photo's album (the source-album hint is UI metadata only).
        if (!await IsAuthorisedForAlbumAsync(userId, photo.AlbumId))
            return Forbid();

        // Cap check before insert — only counts toward the cap on a real (new) add.
        var count = await _cartRepository.CountForUserAsync(userId);
        if (count >= MaxCartItems)
        {
            // Idempotent re-add of an already-present item is fine even at the cap.
            var existingItems = await _cartRepository.GetForUserAsync(userId);
            var alreadyHas = existingItems.Any(i =>
                i.PhotoId == photoId && i.Quality == quality);
            if (!alreadyHas)
            {
                _logger.LogInformation(
                    "User {UserId} hit cart cap ({Limit}); rejecting add of {PhotoId}",
                    userId, MaxCartItems, photoId);
                return Conflict(new CartCapResponse { Reason = "cap_reached", Limit = MaxCartItems });
            }
        }

        var row = new UserCartItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PhotoId = photoId,
            Quality = quality,
            SourceAlbumId = sourceAlbumId ?? photo.AlbumId,
            AddedAt = DateTime.UtcNow
        };

        var persisted = await _cartRepository.AddAsync(row);
        await _cartRepository.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} added cart item {PhotoId} {Quality}", userId, photoId, quality);

        return Ok(new CartItemDto
        {
            PhotoId = persisted.PhotoId.ToString(),
            Quality = persisted.Quality.ToString(),
            SourceAlbumId = persisted.SourceAlbumId?.ToString(),
            SourceAlbumTitle = null,
            FileName = photo.FileName,
            ThumbnailUrl = null,
            AddedAt = persisted.AddedAt
        });
    }

    /// <summary>Remove a single (photoId, quality) row from the current user's cart.</summary>
    [HttpDelete("{photoId:guid}/{quality}")]
    public async Task<IActionResult> RemoveFromCart(Guid photoId, string quality)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (!Enum.TryParse<QualityType>(quality, ignoreCase: true, out var q))
            return BadRequest("Invalid quality.");

        var removed = await _cartRepository.RemoveAsync(userId, photoId, q);
        if (!removed)
            return NotFound();

        await _cartRepository.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Clear the current user's cart entirely.</summary>
    [HttpDelete]
    public async Task<IActionResult> ClearCart()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var n = await _cartRepository.ClearAsync(userId);
        if (n > 0)
            await _cartRepository.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Stream a ZIP of every cart item the user is still authorised to download.
    /// Items the user has lost authorisation for (e.g. saved code expired) are
    /// dropped and listed in the <c>X-Skipped-Photo-Ids</c> response header.
    /// 403 when every item is unauthorised.
    /// </summary>
    [HttpPost("download")]
    public async Task<IActionResult> Download()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var rows = await _cartRepository.GetForUserAsync(userId);
        if (rows.Count == 0)
            return BadRequest("Cart is empty.");

        var skipped = new List<Guid>();
        var validated = new List<CartZipItem>();
        var albumAuthCache = new Dictionary<Guid, bool>();

        foreach (var row in rows)
        {
            if (row.Photo == null) { skipped.Add(row.PhotoId); continue; }
            if (row.Quality == QualityType.Thumbnail) { skipped.Add(row.PhotoId); continue; }

            var albumId = row.Photo.AlbumId;
            if (!albumAuthCache.TryGetValue(albumId, out var ok))
            {
                ok = await IsAuthorisedForAlbumAsync(userId, albumId);
                albumAuthCache[albumId] = ok;
            }
            if (!ok)
            {
                skipped.Add(row.PhotoId);
                continue;
            }

            validated.Add(new CartZipItem
            {
                PhotoId = row.PhotoId,
                AlbumId = albumId,
                FileName = row.Photo.FileName,
                Quality = row.Quality
            });
        }

        if (skipped.Count > 0)
        {
            // Deduplicate header values; comma-separated GUIDs.
            Response.Headers.Append("X-Skipped-Photo-Ids",
                string.Join(",", skipped.Distinct().Select(g => g.ToString())));
        }

        if (validated.Count == 0)
        {
            _logger.LogInformation(
                "User {UserId} cart download — every item ({Total}) is unauthorised at download-time",
                userId, rows.Count);
            return StatusCode(403, "No items in cart are currently authorised for download.");
        }

        var fileName = $"cart-{DateTime.UtcNow:yyyyMMddHHmmss}.zip";
        Response.ContentType = "application/zip";
        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{fileName}\"");

        var bodyControl = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
        if (bodyControl != null) bodyControl.AllowSynchronousIO = true;

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var added = await _cartZipService.StreamCartZipAsync(
            items: validated,
            output: Response.Body,
            accessCodeId: null,
            remoteIp: remoteIp);

        _logger.LogInformation(
            "User {UserId} cart download: {Added}/{Validated} streamed, {Skipped} skipped",
            userId, added, validated.Count, skipped.Count);

        return new EmptyResult();
    }

    /// <summary>
    /// Authorisation predicate: user owns the album, has Admin role, or has a
    /// saved (non-expired) access code that grants the album.
    /// </summary>
    private async Task<bool> IsAuthorisedForAlbumAsync(string userId, Guid albumId)
    {
        if (User.IsInRole("Admin"))
            return true;

        var album = await _albumRepository.GetByIdAsync(albumId);
        if (album == null)
            return false;

        if (album.OwnerId == userId)
            return true;

        var now = DateTime.UtcNow;
        var hasSavedCode = await _db.SavedAccessCodes
            .Include(s => s.AccessCode)
            .AnyAsync(s =>
                s.UserId == userId &&
                s.AccessCode != null &&
                s.AccessCode.AlbumId == albumId &&
                (s.AccessCode.ExpirationDate == null || s.AccessCode.ExpirationDate > now));

        return hasSavedCode;
    }
}

/// <summary>POST /api/cart body.</summary>
public class AddCartItemRequest
{
    public string PhotoId { get; set; } = string.Empty;

    /// <summary>Quality enum name (case-insensitive). Allowed: Low, Medium, High, Original. Thumbnail rejected.</summary>
    public string Quality { get; set; } = string.Empty;

    /// <summary>Optional — the album the user added the photo from (used for cart-UI grouping).</summary>
    public string? SourceAlbumId { get; set; }
}

/// <summary>GET /api/cart response envelope.</summary>
public class CartListResponse
{
    public List<CartItemDto> Items { get; set; } = new();
}

/// <summary>Single cart row as surfaced over the API.</summary>
public class CartItemDto
{
    public string PhotoId { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string? SourceAlbumId { get; set; }
    public string? SourceAlbumTitle { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public DateTime AddedAt { get; set; }
}

/// <summary>409 body when the cart cap is reached.</summary>
public class CartCapResponse
{
    public string Reason { get; set; } = "cap_reached";
    public int Limit { get; set; }
}

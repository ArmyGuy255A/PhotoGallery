using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services;
using PhotoGallery.Services.Processing;
using PhotoGallery.Services.Storage;

namespace PhotoGallery.Controllers;

/// <summary>
/// Public API endpoints for accessing photos via access codes (no authentication required)
/// </summary>
[ApiController]
[Route("api/code")]
[AllowAnonymous]
public class AccessCodeController : ControllerBase
{
    /// <summary>TTL for short-lived public pre-signed URLs (gallery thumbnails).</summary>
    private const int PublicUrlTtlMinutes = 15;

    private readonly IAccessCodeRepository _accessCodeRepository;
    private readonly IPhotoRepository _photoRepository;
    private readonly IRepository<PhotoVersion> _photoVersionRepository;
    private readonly IRepository<Album> _albumRepository;
    private readonly IRepository<UserAccessLog> _accessLogRepository;
    private readonly IStorageProvider _storageProvider;
    private readonly IImageProcessor _imageProcessor;
    private readonly PhotoVersionUrlService _urlService;
    private readonly ICartZipService _cartZipService;
    private readonly ILogger<AccessCodeController> _logger;

    public AccessCodeController(
        IAccessCodeRepository accessCodeRepository,
        IPhotoRepository photoRepository,
        IRepository<PhotoVersion> photoVersionRepository,
        IRepository<Album> albumRepository,
        IRepository<UserAccessLog> accessLogRepository,
        IStorageProvider storageProvider,
        IImageProcessor imageProcessor,
        PhotoVersionUrlService urlService,
        ICartZipService cartZipService,
        ILogger<AccessCodeController> logger)
    {
        _accessCodeRepository = accessCodeRepository;
        _photoRepository = photoRepository;
        _photoVersionRepository = photoVersionRepository;
        _albumRepository = albumRepository;
        _accessLogRepository = accessLogRepository;
        _storageProvider = storageProvider;
        _imageProcessor = imageProcessor;
        _urlService = urlService;
        _cartZipService = cartZipService;
        _logger = logger;
    }

    /// <summary>
    /// Validate an access code and get album info
    /// </summary>
    /// <param name="code">Access code</param>
    /// <returns>Album information if code is valid</returns>
    [HttpGet("{code}/validate")]
    public async Task<ActionResult<CodeValidationResponse>> ValidateCode(string code)
    {
        var accessCode = await _accessCodeRepository.GetByCodeAsync(code);
        if (accessCode == null)
            return NotFound("Access code not found");

        // Check if code is expired
        if (accessCode.ExpirationDate.HasValue && accessCode.ExpirationDate < DateTime.UtcNow)
            return StatusCode(403, "Access code has expired");

        if (accessCode.AlbumId == null)
            return NotFound("Album not found");

        var album = await _albumRepository.GetByIdAsync(accessCode.AlbumId.Value);
        if (album == null)
            return NotFound("Album not found");

        _logger.LogInformation("Access code {Code} validated successfully", code);

        // Log this validation for the Admin access-code analytics view.
        // Capped strings keep abusive User-Agents (or buggy clients sending
        // megabyte UAs) from blowing up the row. Best-effort: a logging
        // failure must not break the user's access to the gallery.
        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var ua = Request.Headers.UserAgent.ToString();
            if (ua.Length > 256) ua = ua.Substring(0, 256);
            if (ip != null && ip.Length > 45) ip = ip.Substring(0, 45);
            await _accessLogRepository.AddAsync(new UserAccessLog
            {
                Id = Guid.NewGuid(),
                AccessCodeId = accessCode.Id,
                AccessDate = DateTime.UtcNow,
                IpAddress = ip,
                UserAgent = string.IsNullOrWhiteSpace(ua) ? null : ua,
                UserId = User.Identity?.IsAuthenticated == true
                    ? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    : null
            });
            await _accessLogRepository.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to write UserAccessLog for code {Code} (non-fatal — validation continues)",
                code);
        }

        return Ok(new CodeValidationResponse
        {
            AlbumId = album.Id.ToString(),
            AlbumTitle = album.Title,
            AlbumDescription = album.Description ?? string.Empty,
            IsValid = true,
            ExpirationDate = accessCode.ExpirationDate
        });
    }

    /// <summary>
    /// Get photos in an album (requires valid access code) with pagination.
    /// Returns short-lived (15-min) pre-signed thumbnail URLs that bypass the long-lived cache,
    /// so revoked access codes don't leak download links.
    ///
    /// Pagination is optional — when omitted, all photos are returned in one page
    /// (preserves backward compatibility).
    /// </summary>
    /// <param name="code">Access code</param>
    /// <param name="page">1-based page number (default 1)</param>
    /// <param name="pageSize">Items per page (default 20, max 100)</param>
    /// <returns>Paginated list of photos in the album</returns>
    [HttpGet("{code}/photos")]
    public async Task<ActionResult<PaginatedPublicPhotosResponse>> GetAlbumPhotos(
        string code,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var accessCode = await _accessCodeRepository.GetByCodeAsync(code);
        if (accessCode == null)
            return NotFound("Access code not found");

        // Check expiration
        if (accessCode.ExpirationDate.HasValue && accessCode.ExpirationDate < DateTime.UtcNow)
            return StatusCode(403, "Access code has expired");

        if (accessCode.AlbumId == null)
            return NotFound("Album not found");

        var album = await _albumRepository.GetByIdAsync(accessCode.AlbumId.Value);
        if (album == null)
            return NotFound("Album not found");

        // Use scoped query (not full table scan)
        var albumPhotos = await _photoRepository.GetAlbumPhotosAsync(accessCode.AlbumId.Value);

        // Sort: FileName ASC so DSC_8000.JPG precedes DSC_8001.JPG. Stable order is
        // required for progressive paging — see AlbumsController for the same rationale.
        var ordered = albumPhotos.OrderBy(p => p.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        var totalCount = ordered.Count;

        var (effectivePage, effectivePageSize) = NormalizePagination(page, pageSize, totalCount);
        var pageStart = (effectivePage - 1) * effectivePageSize;
        var paged = ordered.Skip(pageStart).Take(effectivePageSize).ToList();

        var result = new List<PublicPhotoListDto>(paged.Count);
        foreach (var photo in paged)
        {
            string? thumbnailUrl = null;
            string? mediumUrl = null;
            try
            {
                thumbnailUrl = await _urlService.GenerateShortLivedUrlAsync(
                    photo.Id, QualityType.Thumbnail, PublicUrlTtlMinutes, watermarked: true);
                // Public viewers see the watermarked Medium per D009 (deters AI removal +
                // keeps the unwatermarked variant gated behind cart-checkout).
                mediumUrl = await _urlService.GenerateShortLivedUrlAsync(
                    photo.Id, QualityType.Medium, PublicUrlTtlMinutes, watermarked: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to generate short-lived URLs for photo {PhotoId} (continuing)", photo.Id);
            }

            result.Add(new PublicPhotoListDto
            {
                PhotoId = photo.Id.ToString(),
                FileName = photo.FileName,
                UploadDate = photo.UploadDate,
                ThumbnailUrl = thumbnailUrl,
                MediumUrl = mediumUrl,
                AvailableQualities = await GetAvailableQualities(photo.Id.ToString())
            });
        }

        _logger.LogInformation(
            "Retrieved photos {Start}-{End} of {Total} from album {AlbumId} via code",
            pageStart + 1, pageStart + result.Count, totalCount, album.Id);

        return Ok(new PaginatedPublicPhotosResponse
        {
            Items = result,
            TotalCount = totalCount,
            Page = effectivePage,
            PageSize = effectivePageSize,
            HasMore = pageStart + result.Count < totalCount
        });
    }

    /// <summary>
    /// Clamp pagination params. Same algorithm as AlbumsController.NormalizePagination.
    /// Phase 6: when neither param is supplied callers still get a single default-sized
    /// page (20) with a truthful <c>hasMore</c>, not the full album.
    /// </summary>
    private static (int page, int pageSize) NormalizePagination(int? page, int? pageSize, int totalCount)
    {
        const int defaultPageSize = 20;
        const int maxPageSize = 100;

        var p = page.GetValueOrDefault(1);
        var s = pageSize.GetValueOrDefault(defaultPageSize);

        if (p < 1) p = 1;
        if (s < 1) s = defaultPageSize;
        if (s > maxPageSize) s = maxPageSize;

        return (p, s);
    }

    /// <summary>
    /// Bulk download (cart checkout): stream a ZIP archive containing the requested photo
    /// versions. Photos validated against the album referenced by the access code.
    ///
    /// Each successfully-added photo is logged to the Download table for analytics.
    /// </summary>
    /// <param name="code">Access code</param>
    /// <param name="request">Cart contents (max 100 items, deduped server-side)</param>
    [HttpPost("{code}/cart/download")]
    public async Task<IActionResult> DownloadCart(string code, [FromBody] CartDownloadRequest request)
    {
        if (request?.Items == null || request.Items.Count == 0)
            return BadRequest("Cart is empty.");

        var accessCode = await _accessCodeRepository.GetByCodeAsync(code);
        if (accessCode == null)
            return NotFound("Access code not found");

        if (accessCode.ExpirationDate.HasValue && accessCode.ExpirationDate < DateTime.UtcNow)
            return StatusCode(403, "Access code has expired");

        if (accessCode.AlbumId == null)
            return NotFound("Album not found");

        var album = await _albumRepository.GetByIdAsync(accessCode.AlbumId.Value);
        if (album == null)
            return NotFound("Album not found");

        // Server-side dedupe + validation. Reject Thumbnail (preview only) and any unknown enum.
        // Album membership is enforced here (pre-authorisation) before handing pre-validated
        // items to the shared cart-zip service.
        var seen = new HashSet<(Guid, QualityType)>();
        var validated = new List<CartZipItem>();
        foreach (var item in request.Items)
        {
            if (!Guid.TryParse(item.PhotoId, out var photoGuid))
                continue;
            if (!Enum.TryParse<QualityType>(item.Quality, ignoreCase: true, out var quality))
                continue;
            if (quality == QualityType.Thumbnail)
                continue; // Thumbnail is preview-only, not for download
            var key = (photoGuid, quality);
            if (!seen.Add(key))
                continue;

            var photo = await _photoRepository.GetByIdAsync(photoGuid);
            if (photo == null)
                continue;
            if (photo.AlbumId != accessCode.AlbumId)
            {
                _logger.LogWarning(
                    "Skipping {PhotoId}: does not belong to album {AlbumId} (security check)",
                    photoGuid, accessCode.AlbumId);
                continue;
            }

            validated.Add(new CartZipItem
            {
                PhotoId = photoGuid,
                AlbumId = photo.AlbumId,
                FileName = photo.FileName,
                Quality = quality
            });
        }

        if (validated.Count == 0)
            return BadRequest("No valid items in cart (Thumbnail is not downloadable).");

        if (validated.Count > CartZipService.MaxItemsPerCartConst)
            return BadRequest($"Cart exceeds maximum of {CartZipService.MaxItemsPerCartConst} items.");

        // Build a sanitized download filename. The album title may contain spaces / unicode.
        var safeTitle = string.IsNullOrWhiteSpace(album.Title) ? "album" : album.Title;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safeTitle = safeTitle.Replace(c, '_');
        }
        var fileName = $"{safeTitle}-{DateTime.UtcNow:yyyyMMddHHmmss}.zip";

        Response.ContentType = "application/zip";
        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{fileName}\"");

        // ZipArchive.Dispose performs synchronous writes to finalize the central
        // directory, which Kestrel rejects by default with InvalidOperationException
        // ""Synchronous operations are disallowed"". Enabling AllowSynchronousIO
        // for *this* request only is the documented escape hatch — it scopes to the
        // current HttpContext and does not affect global Kestrel settings. The
        // alternative (buffering the entire ZIP to a MemoryStream) would consume
        // up to MaxItemsPerCart * full-resolution-photo-bytes of memory per
        // concurrent request, which is unbounded for prod.
        var bodyControl = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
        if (bodyControl != null) bodyControl.AllowSynchronousIO = true;

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var added = await _cartZipService.StreamCartZipAsync(
            items: validated,
            output: Response.Body,
            accessCodeId: accessCode.Id,
            remoteIp: remoteIp);

        _logger.LogInformation(
            "Cart download complete: {Added}/{Requested} items, code={Code}, album={AlbumId}",
            added, validated.Count, code, album.Id);

        return new EmptyResult();
    }

    /// <summary>
    /// Download a photo using an access code
    /// </summary>
    /// <param name="code">Access code</param>
    /// <param name="photoId">Photo ID</param>
    /// <param name="quality">Quality level: high, medium, low, or raw</param>
    /// <returns>Photo file stream</returns>
    [HttpGet("{code}/photo/{photoId}/download")]
    public async Task<ActionResult> DownloadPhotoByCode(string code, string photoId, [FromQuery] string quality = "medium")
    {
        if (!Guid.TryParse(photoId, out var photoGuid))
            return BadRequest("Invalid photo ID");

        var accessCode = await _accessCodeRepository.GetByCodeAsync(code);
        if (accessCode == null)
            return NotFound("Access code not found");

        // Check expiration
        if (accessCode.ExpirationDate.HasValue && accessCode.ExpirationDate < DateTime.UtcNow)
            return StatusCode(403, "Access code has expired");

        var photo = await _photoRepository.GetByIdAsync(photoGuid);
        if (photo == null)
            return NotFound("Photo not found");

        // Verify photo belongs to the album referenced by the access code
        if (photo.AlbumId != accessCode.AlbumId)
            return NotFound("Photo not found in this album");

        try
        {
            var photoVersion = await _imageProcessor.GetPhotoVersionAsync(photoId, quality);
            if (photoVersion == null)
                return NotFound($"Photo version not found for quality: {quality}");

            var stream = await _storageProvider.DownloadAsync(photoVersion.StorageKey);

            _logger.LogInformation(
                "Photo {PhotoId} downloaded via code {Code} at {Quality} quality",
                photoId, code, quality);

            return File(stream, "image/jpeg", $"{photo.FileName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading photo {PhotoId} via code", photoId);
            return StatusCode(500, "Error downloading photo");
        }
    }

    /// <summary>
    /// Get available compression profiles for selecting download quality
    /// </summary>
    /// <returns>List of available compression profiles</returns>
    [HttpGet("compression-profiles")]
    public ActionResult<IEnumerable<CompressionProfileDto>> GetCompressionProfiles()
    {
        var profiles = _imageProcessor.GetCompressionProfiles();
        return Ok(profiles.Select(p => new CompressionProfileDto
        {
            Name = p.Name,
            QualityPercentage = p.QualityPercentage,
            Description = p.Description
        }));
    }

    private async Task<List<string>> GetAvailableQualities(string photoId)
    {
        var qualities = new List<string>();
        // Note: "original" is surfaced when the original.jpg object exists in storage,
        // so cart-eligible callers can offer Original as a download choice (PR-B / bug #7).
        // "thumbnail" is intentionally omitted — it is preview-only and rejected by DownloadCart.
        foreach (var qualityName in new[] { "high", "medium", "low", "raw", "original" })
        {
            var version = await _imageProcessor.GetPhotoVersionAsync(photoId, qualityName);
            if (version != null)
                qualities.Add(qualityName);
        }
        return qualities;
    }
}

/// <summary>
/// DTO for code validation response
/// </summary>
public class CodeValidationResponse
{
    public string AlbumId { get; set; } = string.Empty;
    public string AlbumTitle { get; set; } = string.Empty;
    public string AlbumDescription { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public DateTime? ExpirationDate { get; set; }
}

/// <summary>
/// DTO for photo list item (legacy, retained for backward compatibility)
/// </summary>
public class PhotoListItemDto
{
    public string PhotoId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    public List<string> AvailableQualities { get; set; } = new();
}

/// <summary>
/// DTO for public photo list (returned to access-code clients).
/// Includes short-lived (15-min) thumbnail and medium URLs so the browser can load images
/// directly from blob storage without proxying through the web server.
/// </summary>
public class PublicPhotoListDto
{
    public string PhotoId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? MediumUrl { get; set; }
    public List<string> AvailableQualities { get; set; } = new();
}

/// <summary>
/// Paginated response envelope for public photo lists.
/// </summary>
public class PaginatedPublicPhotosResponse
{
    /// <summary>The current page of photos. Serialized as <c>items</c> per Phase 6 contract.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public List<PublicPhotoListDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>
/// Cart bulk-download request body.
/// </summary>
public class CartDownloadRequest
{
    public List<CartDownloadItem> Items { get; set; } = new();
}

/// <summary>
/// Single item in a cart download request.
/// </summary>
public class CartDownloadItem
{
    public string PhotoId { get; set; } = string.Empty;

    /// <summary>Quality enum name (case-insensitive): Low, Medium, or High. Thumbnail rejected.</summary>
    public string Quality { get; set; } = string.Empty;
}

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
    private readonly IStorageProvider _storageProvider;
    private readonly IImageProcessor _imageProcessor;
    private readonly PhotoVersionUrlService _urlService;
    private readonly ZipDownloadService _zipService;
    private readonly ILogger<AccessCodeController> _logger;

    public AccessCodeController(
        IAccessCodeRepository accessCodeRepository,
        IPhotoRepository photoRepository,
        IRepository<PhotoVersion> photoVersionRepository,
        IRepository<Album> albumRepository,
        IStorageProvider storageProvider,
        IImageProcessor imageProcessor,
        PhotoVersionUrlService urlService,
        ZipDownloadService zipService,
        ILogger<AccessCodeController> logger)
    {
        _accessCodeRepository = accessCodeRepository;
        _photoRepository = photoRepository;
        _photoVersionRepository = photoVersionRepository;
        _albumRepository = albumRepository;
        _storageProvider = storageProvider;
        _imageProcessor = imageProcessor;
        _urlService = urlService;
        _zipService = zipService;
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

        var album = await _albumRepository.GetByIdAsync(accessCode.AlbumId);
        if (album == null)
            return NotFound("Album not found");

        _logger.LogInformation("Access code {Code} validated successfully", code);

        return Ok(new CodeValidationResponse
        {
            AlbumId = album.Id.ToString(),
            AlbumTitle = album.Title,
            AlbumDescription = album.Description,
            IsValid = true,
            ExpirationDate = accessCode.ExpirationDate
        });
    }

    /// <summary>
    /// Get all photos in an album (requires valid access code).
    /// Returns short-lived (15-min) pre-signed thumbnail URLs that bypass the long-lived cache,
    /// so revoked access codes don't leak download links.
    /// </summary>
    /// <param name="code">Access code</param>
    /// <returns>List of photos in the album</returns>
    [HttpGet("{code}/photos")]
    public async Task<ActionResult<List<PublicPhotoListDto>>> GetAlbumPhotos(string code)
    {
        var accessCode = await _accessCodeRepository.GetByCodeAsync(code);
        if (accessCode == null)
            return NotFound("Access code not found");

        // Check expiration
        if (accessCode.ExpirationDate.HasValue && accessCode.ExpirationDate < DateTime.UtcNow)
            return StatusCode(403, "Access code has expired");

        var album = await _albumRepository.GetByIdAsync(accessCode.AlbumId);
        if (album == null)
            return NotFound("Album not found");

        // Use scoped query (not full table scan)
        var albumPhotos = await _photoRepository.GetAlbumPhotosAsync(accessCode.AlbumId);

        var result = new List<PublicPhotoListDto>();
        foreach (var photo in albumPhotos)
        {
            string? thumbnailUrl = null;
            try
            {
                thumbnailUrl = await _urlService.GenerateShortLivedUrlAsync(
                    photo.Id, QualityType.Thumbnail, PublicUrlTtlMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to generate short-lived thumbnail URL for photo {PhotoId} (continuing)", photo.Id);
            }

            result.Add(new PublicPhotoListDto
            {
                PhotoId = photo.Id.ToString(),
                FileName = photo.FileName,
                UploadDate = photo.UploadDate,
                ThumbnailUrl = thumbnailUrl,
                AvailableQualities = await GetAvailableQualities(photo.Id.ToString())
            });
        }

        _logger.LogInformation("Retrieved {PhotoCount} photos from album {AlbumId} via code", albumPhotos.Count, album.Id);

        return Ok(result);
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

        var album = await _albumRepository.GetByIdAsync(accessCode.AlbumId);
        if (album == null)
            return NotFound("Album not found");

        // Server-side dedupe + validation. Reject Thumbnail (preview only) and any unknown enum.
        var seen = new HashSet<(Guid, QualityType)>();
        var validated = new List<CartItem>();
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
            validated.Add(new CartItem { PhotoId = photoGuid, Quality = quality });
        }

        if (validated.Count == 0)
            return BadRequest("No valid items in cart (Thumbnail is not downloadable).");

        if (validated.Count > ZipDownloadService.MaxItemsPerCart)
            return BadRequest($"Cart exceeds maximum of {ZipDownloadService.MaxItemsPerCart} items.");

        // Build a sanitized download filename. The album title may contain spaces / unicode.
        var safeTitle = string.IsNullOrWhiteSpace(album.Title) ? "album" : album.Title;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safeTitle = safeTitle.Replace(c, '_');
        }
        var fileName = $"{safeTitle}-{DateTime.UtcNow:yyyyMMddHHmmss}.zip";

        Response.ContentType = "application/zip";
        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{fileName}\"");

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var added = await _zipService.StreamCartZipAsync(
            albumId: album.Id,
            accessCodeId: accessCode.Id,
            items: validated,
            output: Response.Body,
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
        foreach (var qualityName in new[] { "high", "medium", "low", "raw" })
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
/// Includes a short-lived (15-min) thumbnail URL so the browser can load images
/// directly from blob storage without proxying through the web server.
/// </summary>
public class PublicPhotoListDto
{
    public string PhotoId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    public string? ThumbnailUrl { get; set; }
    public List<string> AvailableQualities { get; set; } = new();
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

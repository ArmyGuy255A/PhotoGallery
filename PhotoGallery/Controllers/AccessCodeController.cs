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
    private readonly IRepository<Download> _downloadRepository;
    private readonly ILogger<AccessCodeController> _logger;

    /// <summary>Maximum number of items allowed in a single cart download / manifest request.</summary>
    public const int MaxItemsPerCart = 100;

    /// <summary>Short-lived TTL (minutes) for cart-manifest pre-signed URLs.</summary>
    private const int CartManifestUrlTtlMinutes = 15;

    public AccessCodeController(
        IAccessCodeRepository accessCodeRepository,
        IPhotoRepository photoRepository,
        IRepository<PhotoVersion> photoVersionRepository,
        IRepository<Album> albumRepository,
        IStorageProvider storageProvider,
        IImageProcessor imageProcessor,
        PhotoVersionUrlService urlService,
        IRepository<Download> downloadRepository,
        ILogger<AccessCodeController> logger)
    {
        _accessCodeRepository = accessCodeRepository;
        _photoRepository = photoRepository;
        _photoVersionRepository = photoVersionRepository;
        _albumRepository = albumRepository;
        _storageProvider = storageProvider;
        _imageProcessor = imageProcessor;
        _urlService = urlService;
        _downloadRepository = downloadRepository;
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

        var album = await _albumRepository.GetByIdAsync(accessCode.AlbumId);
        if (album == null)
            return NotFound("Album not found");

        // Use scoped query (not full table scan)
        var albumPhotos = await _photoRepository.GetAlbumPhotosAsync(accessCode.AlbumId);

        // Sort: newest first
        var ordered = albumPhotos.OrderByDescending(p => p.UploadDate).ToList();
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
                    photo.Id, QualityType.Thumbnail, PublicUrlTtlMinutes);
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
            Photos = result,
            TotalCount = totalCount,
            Page = effectivePage,
            PageSize = effectivePageSize,
            HasMore = pageStart + result.Count < totalCount
        });
    }

    /// <summary>
    /// Clamp pagination params. Same algorithm as AlbumsController.NormalizePagination.
    /// </summary>
    private static (int page, int pageSize) NormalizePagination(int? page, int? pageSize, int totalCount)
    {
        const int defaultPageSize = 20;
        const int maxPageSize = 100;

        if (!page.HasValue && !pageSize.HasValue)
        {
            return (1, Math.Max(totalCount, 1));
        }

        var p = page.GetValueOrDefault(1);
        var s = pageSize.GetValueOrDefault(defaultPageSize);

        if (p < 1) p = 1;
        if (s < 1) s = defaultPageSize;
        if (s > maxPageSize) s = maxPageSize;

        return (p, s);
    }

    /// <summary>
    /// Build a download manifest for a cart. The browser uses the returned pre-signed URLs
    /// to stream blobs directly from storage and assemble a ZIP locally (client-zip).
    ///
    /// Reuses every validation rule the legacy <see cref="DownloadCart"/> action enforced:
    /// access-code lookup + expiration, album-scope (photo must belong to the code's album),
    /// dedupe on (photoId, quality), Thumbnail rejection, 100-cap, and sanitized filenames
    /// with collision suffixes (mirrors the <c>usedNames</c> pattern from
    /// <see cref="BuildManifestEntryName"/>).
    ///
    /// Pre-signed URLs are short-lived (15 min) and never watermarked — cart is the
    /// paid-checkout path. <see cref="PhotoVersionUrlService"/> additionally refuses to serve
    /// watermarked Original even if a buggy caller asks for it.
    ///
    /// Per-item analytics: one <see cref="Download"/> row is logged when the URL is *issued*,
    /// because browser-side completion is not reliably observable. This aligns with how
    /// presigned URLs work everywhere else in the app.
    /// </summary>
    /// <param name="code">Access code</param>
    /// <param name="request">Cart contents (max 100 items, deduped server-side)</param>
    [HttpPost("{code}/cart/manifest")]
    public async Task<ActionResult<CartManifestResponse>> CartManifest(
        string code,
        [FromBody] CartManifestRequest request)
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

        // Server-side dedupe + validation (mirrors legacy DownloadCart).
        var seen = new HashSet<(Guid, QualityType)>();
        var validated = new List<(Guid PhotoId, QualityType Quality)>();
        foreach (var item in request.Items)
        {
            if (!Guid.TryParse(item.PhotoId, out var photoGuid))
                continue;
            if (!Enum.TryParse<QualityType>(item.Quality, ignoreCase: true, out var quality))
                continue;
            if (quality == QualityType.Thumbnail)
                continue; // Thumbnail is preview-only, never downloadable
            if (!seen.Add((photoGuid, quality)))
                continue;
            validated.Add((photoGuid, quality));
        }

        if (validated.Count == 0)
            return BadRequest("No valid items in cart (Thumbnail is not downloadable).");

        if (validated.Count > MaxItemsPerCart)
            return BadRequest($"Cart exceeds maximum of {MaxItemsPerCart} items.");

        // Build the prefix the FE uses to name the resulting ZIP file (e.g. "Wedding_2026-20260509181734").
        var safeTitle = string.IsNullOrWhiteSpace(album.Title) ? "album" : album.Title;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safeTitle = safeTitle.Replace(c, '_');
        }
        var fileNamePrefix = $"{safeTitle}-{DateTime.UtcNow:yyyyMMddHHmmss}";

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ipHash = HashIp(remoteIp);

        var entries = new List<CartManifestEntry>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var issuedDownloads = 0;

        foreach (var (photoId, quality) in validated)
        {
            try
            {
                var photo = await _photoRepository.GetByIdAsync(photoId);
                if (photo == null)
                {
                    _logger.LogWarning("Skipping {PhotoId}: photo not found", photoId);
                    continue;
                }
                if (photo.AlbumId != album.Id)
                {
                    // Cross-album reference — security check (caller passed a foreign photo id).
                    _logger.LogWarning(
                        "Skipping {PhotoId}: does not belong to album {AlbumId} (security check)",
                        photoId, album.Id);
                    continue;
                }

                var url = await _urlService.GenerateShortLivedUrlAsync(
                    photoId, quality, CartManifestUrlTtlMinutes, watermarked: false);
                if (string.IsNullOrEmpty(url))
                {
                    _logger.LogWarning(
                        "Skipping {PhotoId} {Quality}: short-lived URL could not be generated",
                        photoId, quality);
                    continue;
                }

                var entryName = BuildManifestEntryName(photo.FileName, quality, usedNames);

                entries.Add(new CartManifestEntry
                {
                    PhotoId = photoId.ToString(),
                    Quality = quality.ToString(),
                    FileName = entryName,
                    Url = url
                });

                // B6: log Download per *URL issued* — browser completion is not observable.
                await _downloadRepository.AddAsync(new Download
                {
                    Id = Guid.NewGuid(),
                    PhotoId = photoId,
                    AccessCodeId = accessCode.Id,
                    Quality = quality,
                    DownloadedAt = DateTime.UtcNow,
                    IpHash = ipHash
                });
                issuedDownloads++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error preparing manifest entry for {PhotoId} {Quality} — skipping",
                    photoId, quality);
                // Continue with remaining items — partial manifest is better than none.
            }
        }

        if (issuedDownloads > 0)
        {
            await _downloadRepository.SaveChangesAsync();
        }

        _logger.LogInformation(
            "Cart manifest issued: {Issued}/{Requested} items, code={Code}, album={AlbumId}",
            entries.Count, validated.Count, code, album.Id);

        return Ok(new CartManifestResponse
        {
            AlbumTitle = album.Title ?? string.Empty,
            FileNamePrefix = fileNamePrefix,
            Items = entries
        });
    }

    /// <summary>
    /// Sanitize and disambiguate a manifest entry filename. Mirrors the legacy
    /// server-side ZIP layout (Quality/Name.ext, suffix on collision) so the ZIP the
    /// browser produces matches the file structure consumers were getting before.
    /// </summary>
    internal static string BuildManifestEntryName(string originalFileName, QualityType quality, HashSet<string> used)
    {
        var safeName = string.IsNullOrWhiteSpace(originalFileName)
            ? "photo.jpg"
            : SanitizeFileName(originalFileName);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";

        var qualityFolder = quality.ToString();
        var candidate = $"{qualityFolder}/{nameWithoutExt}{ext}";

        var counter = 1;
        while (!used.Add(candidate))
        {
            candidate = $"{qualityFolder}/{nameWithoutExt}_{counter}{ext}";
            counter++;
        }

        return candidate;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (Array.IndexOf(invalid, c) < 0 && c != '/' && c != '\\')
                sb.Append(c);
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? "photo.jpg" : result;
    }

    private static string HashIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return string.Empty;
        var bytes = System.Text.Encoding.UTF8.GetBytes(ip);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
    public List<PublicPhotoListDto> Photos { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>
/// Cart manifest request body. The FE POSTs the cart contents and receives a list
/// of pre-signed URLs the browser uses to fetch each blob directly from storage.
/// </summary>
public class CartManifestRequest
{
    public List<CartManifestItem> Items { get; set; } = new();
}

/// <summary>
/// Single item in a cart manifest request.
/// </summary>
public class CartManifestItem
{
    public string PhotoId { get; set; } = string.Empty;

    /// <summary>Quality enum name (case-insensitive): Low, Medium, High, Original. Thumbnail rejected.</summary>
    public string Quality { get; set; } = string.Empty;
}

/// <summary>
/// Response body for <c>POST /api/code/{code}/cart/manifest</c>. Browser uses these
/// short-lived pre-signed URLs to fetch each blob and assemble a ZIP locally
/// (client-zip). <see cref="FileNamePrefix"/> is the suggested ZIP base name.
/// </summary>
public class CartManifestResponse
{
    public string AlbumTitle { get; set; } = string.Empty;
    public string FileNamePrefix { get; set; } = string.Empty;
    public List<CartManifestEntry> Items { get; set; } = new();
}

/// <summary>
/// Single resolved manifest entry: enough information for the browser to
/// fetch the blob, place it under the ZIP entry path, and report progress.
/// </summary>
public class CartManifestEntry
{
    public string PhotoId { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;

    /// <summary>Sanitized, collision-suffixed ZIP entry name (e.g. <c>Medium/IMG_0001.jpg</c>).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Short-lived (15 min) pre-signed storage URL. Never watermarked.</summary>
    public string Url { get; set; } = string.Empty;
}

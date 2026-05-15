using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services;
using System.Security.Claims;

namespace PhotoGallery.Controllers;

/// <summary>
/// API endpoints for album management (authenticated users only)
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AlbumsController : ControllerBase
{
    private readonly IAlbumRepository _albumRepository;
    private readonly IPhotoRepository _photoRepository;
    private readonly IAccessCodeRepository _accessCodeRepository;
    private readonly PhotoVersionUrlService _urlService;
    private readonly IUserDisplayNameResolver _displayNames;
    private readonly ILogger<AlbumsController> _logger;

    public AlbumsController(
        IAlbumRepository albumRepository,
        IPhotoRepository photoRepository,
        IAccessCodeRepository accessCodeRepository,
        PhotoVersionUrlService urlService,
        IUserDisplayNameResolver displayNames,
        ILogger<AlbumsController> logger)
    {
        _albumRepository = albumRepository;
        _photoRepository = photoRepository;
        _accessCodeRepository = accessCodeRepository;
        _urlService = urlService;
        _displayNames = displayNames;
        _logger = logger;
    }

    /// <summary>
    /// Get all albums for the current user
    /// </summary>
    /// <returns>List of user's albums</returns>
    [HttpGet]
    public async Task<ActionResult<List<AlbumListDto>>> GetAlbums()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var allAlbums = await _albumRepository.GetAllAsync();
        
        // Admins see all albums; regular users see only their own
        var userAlbums = User.IsInRole("Admin")
            ? allAlbums.ToList()
            : allAlbums.Where(a => a.OwnerId == userId).ToList();

        // Resolve CreatedBy ids -> display names in one batched pass. Listing
        // N albums by the same uploader does 1 DB lookup, not N.
        var displayNames = await _displayNames.ResolveManyAsync(
            userAlbums.Select(a => a.CreatedBy));

        var result = userAlbums.Select(a => new AlbumListDto
        {
            Id = a.Id.ToString(),
            Title = a.Title,
            Description = a.Description ?? string.Empty,
            CreatedDate = a.CreatedDate,
            OwnerId = a.OwnerId,
            CreatedBy = a.CreatedBy,
            CreatedByDisplayName = LookupDisplayName(displayNames, a.CreatedBy),
            CanManage = a.OwnerId == userId || User.IsInRole("Admin")
        }).ToList();

        _logger.LogInformation("Retrieved {AlbumCount} albums for user {UserId}", result.Count, userId);
        return Ok(result);
    }

    /// <summary>
    /// Create a new album (admin only)
    /// </summary>
    /// <param name="request">Album creation request</param>
    /// <returns>Created album details</returns>
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<AlbumDetailDto>> CreateAlbum([FromBody] CreateAlbumRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Album title is required");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var album = new Album
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Description = request.Description,
            OwnerId = userId,
            CreatedBy = userId,
            CreatedDate = DateTime.UtcNow
        };

        await _albumRepository.AddAsync(album);

        await _albumRepository.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAlbumById), new { id = album.Id }, await MapToDetailDtoAsync(album));
    }

    /// <summary>
    /// Get album details by ID
    /// </summary>
    /// <param name="id">Album ID</param>
    /// <returns>Album details</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<AlbumDetailDto>> GetAlbumById(string id)
    {
        if (!Guid.TryParse(id, out var albumId))
            return BadRequest("Invalid album ID");

        var album = await _albumRepository.GetByIdAsync(albumId);
        if (album == null)
            return NotFound("Album not found");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User.IsInRole("Admin");

        // Users can only view albums they own (admins can view any)
        if (album.OwnerId != userId && !isAdmin)
            return Forbid();

        _logger.LogInformation("Album {AlbumId} retrieved by {UserId}", albumId, userId);
        return Ok(await MapToDetailDtoAsync(album));
    }

    /// <summary>
    /// Update album details (admin only)
    /// </summary>
    /// <param name="id">Album ID</param>
    /// <param name="request">Update request</param>
    /// <returns>Updated album details</returns>
    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<ActionResult<AlbumDetailDto>> UpdateAlbum(string id, [FromBody] UpdateAlbumRequest request)
    {
        if (!Guid.TryParse(id, out var albumId))
            return BadRequest("Invalid album ID");

        var album = await _albumRepository.GetByIdAsync(albumId);
        if (album == null)
            return NotFound("Album not found");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        // Admins can manage any album (matches the established pattern used by
        // [Authorize] read endpoints above). Without the !isAdmin escape, an
        // admin can only update albums they personally created — which breaks
        // multi-admin tenancy (e.g. an album created during a DISABLE_AUTH=true
        // dev session is locked to testadmin@localhost forever).
        if (album.OwnerId != userId && !User.IsInRole("Admin"))
            return Forbid();

        if (!string.IsNullOrWhiteSpace(request.Title))
            album.Title = request.Title;

        if (request.Description != null)
            album.Description = request.Description;

        await _albumRepository.UpdateAsync(album);
        await _albumRepository.SaveChangesAsync();
        _logger.LogInformation("Album {AlbumId} updated by {UserId}", albumId, userId);

        return Ok(await MapToDetailDtoAsync(album));
    }

    /// <summary>
    /// Delete an album (admin only)
    /// </summary>
    /// <param name="id">Album ID</param>
    /// <returns>No content</returns>
    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteAlbum(string id)
    {
        if (!Guid.TryParse(id, out var albumId))
            return BadRequest("Invalid album ID");

        var album = await _albumRepository.GetByIdAsync(albumId);
        if (album == null)
            return NotFound("Album not found");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        // Admins can manage any album — see UpdateAlbum comment.
        if (album.OwnerId != userId && !User.IsInRole("Admin"))
            return Forbid();

        // EF Core cascades Album -> Photo + AccessCode automatically, but the
        // photo-level RESTRICT-FK children (Downloads, UserCartItems,
        // ProcessingQueueItems, ProcessingQueues) block the cascade with
        // FK constraint errors. Scrub them in album scope first, mirroring
        // what DeletePhoto does for a single photo.
        var ctx = HttpContext.RequestServices.GetRequiredService<PhotoGallery.Data.ApplicationDbContext>();
        var photoIds = await ctx.Photos
            .Where(p => p.AlbumId == albumId)
            .Select(p => p.Id)
            .ToListAsync();
        if (photoIds.Count > 0)
        {
            var downloads = await ctx.Downloads.Where(d => photoIds.Contains(d.PhotoId)).ToListAsync();
            if (downloads.Count > 0) ctx.Downloads.RemoveRange(downloads);
            var cartItems = await ctx.UserCartItems.Where(c => photoIds.Contains(c.PhotoId)).ToListAsync();
            if (cartItems.Count > 0) ctx.UserCartItems.RemoveRange(cartItems);
            var queueItems = await ctx.ProcessingQueueItems.Where(i => photoIds.Contains(i.PhotoId)).ToListAsync();
            if (queueItems.Count > 0) ctx.ProcessingQueueItems.RemoveRange(queueItems);
            var queues = await ctx.ProcessingQueues.Where(q => photoIds.Contains(q.PhotoId)).ToListAsync();
            if (queues.Count > 0) ctx.ProcessingQueues.RemoveRange(queues);
            await ctx.SaveChangesAsync();
        }

        await _albumRepository.DeleteAsync(album);
        await _albumRepository.SaveChangesAsync();
        _logger.LogInformation("Album {AlbumId} deleted by {UserId}", albumId, userId);

        return NoContent();
    }

    /// <summary>
    /// Get photos in an album with pagination.
    /// Pagination parameters are optional — when omitted, all photos are returned in one page
    /// (preserves backward compatibility with the original list response).
    /// </summary>
    /// <param name="albumId">Album ID</param>
    /// <param name="page">1-based page number (default 1)</param>
    /// <param name="pageSize">Items per page (default 20, max 100)</param>
    /// <returns>Paginated list of photos with pre-signed URLs</returns>
    [HttpGet("{albumId}/photos")]
    public async Task<ActionResult<PaginatedPhotosResponse>> GetAlbumPhotos(
        string albumId,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        if (!Guid.TryParse(albumId, out var albumGuid))
            return BadRequest("Invalid album ID");

        var album = await _albumRepository.GetByIdAsync(albumGuid);
        if (album == null)
            return NotFound("Album not found");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User.IsInRole("Admin");

        // Users can only view albums they own (admins can view any)
        if (album.OwnerId != userId && !isAdmin)
            return Forbid();

        // Album-scoped query (replaces full table scan)
        var albumPhotos = await _photoRepository.GetAlbumPhotosAsync(albumGuid);

        // Sort: FileName ASC so DSC_8000.JPG precedes DSC_8001.JPG. The server-side
        // pin keeps page boundaries stable as the user scrolls. Client-side sort
        // would scramble the streaming order across page fetches.
        var ordered = albumPhotos.OrderBy(p => p.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        var totalCount = ordered.Count;

        // Apply pagination if requested
        var (effectivePage, effectivePageSize) = NormalizePagination(page, pageSize, totalCount);
        var pageStart = (effectivePage - 1) * effectivePageSize;
        var paged = ordered.Skip(pageStart).Take(effectivePageSize).ToList();

        var result = new List<PhotoListDto>(paged.Count);
        foreach (var p in paged)
        {
            var photoDto = new PhotoListDto
            {
                Id = p.Id.ToString(),
                FileName = p.FileName,
                UploadDate = p.UploadDate,
                UploadedBy = p.UploadedBy
            };

            try
            {
                photoDto.ThumbnailUrl = await _urlService.GetPhotoVersionUrlAsync(p.Id, Enums.QualityType.Thumbnail);
                photoDto.MediumUrl = await _urlService.GetPhotoVersionUrlAsync(p.Id, Enums.QualityType.Medium);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get pre-signed URLs for photo {PhotoId}", p.Id);
            }

            result.Add(photoDto);
        }

        _logger.LogInformation(
            "Retrieved photos {Start}-{End} of {Total} for album {AlbumId}",
            pageStart + 1, pageStart + result.Count, totalCount, albumGuid);

        return Ok(new PaginatedPhotosResponse
        {
            Items = result,
            TotalCount = totalCount,
            Page = effectivePage,
            PageSize = effectivePageSize,
            HasMore = pageStart + result.Count < totalCount
        });
    }

    /// <summary>
    /// Clamp pagination params to safe defaults. Used by both album and (future) other endpoints.
    /// When neither <paramref name="page"/> nor <paramref name="pageSize"/> is supplied the
    /// caller still receives a single default-sized page (20). This is deliberate — Phase 6
    /// makes the photo grid progressive, so the full-list response is no longer the
    /// implicit contract; older clients that omit pagination see the first 20 items and a
    /// truthful <c>hasMore</c> rather than a silent full-table dump.
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
    /// Get all access codes for an album (admin only)
    /// </summary>
    /// <param name="albumId">Album ID</param>
    /// <returns>List of access codes</returns>
    [Authorize(Roles = "Admin")]
    [HttpGet("{albumId}/access-codes")]
    public async Task<ActionResult<List<AccessCodeListDto>>> GetAccessCodes(string albumId)
    {
        if (!Guid.TryParse(albumId, out var albumGuid))
            return BadRequest("Invalid album ID");

        var album = await _albumRepository.GetByIdAsync(albumGuid);
        if (album == null)
            return NotFound("Album not found");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        // Admins can manage any album — see UpdateAlbum comment.
        if (album.OwnerId != userId && !User.IsInRole("Admin"))
            return Forbid();

        var allCodes = await _accessCodeRepository.GetAllAsync();
        var albumCodesEntities = allCodes.Where(c => c.AlbumId == albumGuid).ToList();

        var codeDisplayNames = await _displayNames.ResolveManyAsync(
            albumCodesEntities.Select(c => c.CreatedBy));

        var albumCodes = albumCodesEntities
            .Select(c => new AccessCodeListDto
            {
                Id = c.Id.ToString(),
                Code = c.Code,
                ExpirationDate = c.ExpirationDate,
                CreatedDate = c.CreatedDate,
                CreatedBy = c.CreatedBy,
                CreatedByDisplayName = LookupDisplayName(codeDisplayNames, c.CreatedBy),
                IsExpired = c.ExpirationDate.HasValue && c.ExpirationDate < DateTime.UtcNow
            })
            .ToList();

        _logger.LogInformation("Retrieved {CodeCount} access codes for album {AlbumId}", albumCodes.Count, albumGuid);
        return Ok(albumCodes);
    }

    /// <summary>
    /// Create a new access code for an album (admin only)
    /// </summary>
    /// <param name="albumId">Album ID</param>
    /// <param name="request">Access code creation request</param>
    /// <returns>Created access code details</returns>
    [Authorize(Roles = "Admin")]
    [HttpPost("{albumId}/access-codes")]
    public async Task<ActionResult<AccessCodeDetailDto>> CreateAccessCode(string albumId, [FromBody] CreateAccessCodeRequest request)
    {
        if (!Guid.TryParse(albumId, out var albumGuid))
            return BadRequest("Invalid album ID");

        var album = await _albumRepository.GetByIdAsync(albumGuid);
        if (album == null)
            return NotFound("Album not found");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        // Admins can manage any album — see UpdateAlbum comment.
        if (album.OwnerId != userId && !User.IsInRole("Admin"))
            return Forbid();

        // Generate unique code
        string code;
        bool codeExists;
        do
        {
            code = GenerateAccessCode();
            var existingCodes = await _accessCodeRepository.GetAllAsync();
            codeExists = existingCodes.Any(c => c.Code == code);
        } while (codeExists);

        // Determine expiration date:
        // 1. ExpiresForever → null
        // 2. ExpirationDate provided (and in the future) → use it
        // 3. ExpirationDays provided → today + N days
        // 4. Default → today + 30 days
        DateTime? expirationDate;
        if (request.ExpiresForever)
        {
            expirationDate = null;
        }
        else if (request.ExpirationDate.HasValue)
        {
            var requestedDate = DateTime.SpecifyKind(request.ExpirationDate.Value, DateTimeKind.Utc);
            if (requestedDate <= DateTime.UtcNow)
            {
                return BadRequest("Expiration date must be in the future.");
            }
            expirationDate = requestedDate;
        }
        else
        {
            expirationDate = DateTime.UtcNow.AddDays(request.ExpirationDays ?? 30);
        }

        var accessCode = new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = albumGuid,
            Code = code,
            ExpirationDate = expirationDate,
            CreatedDate = DateTime.UtcNow,
            CreatedBy = userId
        };

        await _accessCodeRepository.AddAsync(accessCode);
        await _accessCodeRepository.SaveChangesAsync();
        _logger.LogInformation("Access code {CodeId} created for album {AlbumId} by {UserId}", accessCode.Id, albumGuid, userId);

        return CreatedAtAction(nameof(GetAccessCodes), new { albumId }, new AccessCodeDetailDto
        {
            Id = accessCode.Id.ToString(),
            Code = accessCode.Code,
            ExpirationDate = accessCode.ExpirationDate,
            CreatedDate = accessCode.CreatedDate,
            CreatedBy = accessCode.CreatedBy,
            CreatedByDisplayName = await _displayNames.ResolveAsync(accessCode.CreatedBy),
            IsExpired = false
        });
    }

    /// <summary>
    /// Delete an access code (admin only)
    /// </summary>
    /// <param name="albumId">Album ID</param>
    /// <param name="codeId">Access code ID</param>
    /// <returns>No content</returns>
    [Authorize(Roles = "Admin")]
    [HttpDelete("{albumId}/access-codes/{codeId}")]
    public async Task<ActionResult> DeleteAccessCode(string albumId, string codeId)
    {
        if (!Guid.TryParse(albumId, out var albumGuid) || !Guid.TryParse(codeId, out var codeGuid))
            return BadRequest("Invalid album or code ID");

        var album = await _albumRepository.GetByIdAsync(albumGuid);
        if (album == null)
            return NotFound("Album not found");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        // Admins can manage any album — see UpdateAlbum comment.
        if (album.OwnerId != userId && !User.IsInRole("Admin"))
            return Forbid();

        var accessCode = await _accessCodeRepository.GetByIdAsync(codeGuid);
        if (accessCode == null)
            return NotFound("Access code not found");

        if (accessCode.AlbumId != albumGuid)
            return BadRequest("Access code does not belong to this album");

        await _accessCodeRepository.DeleteAsync(accessCode);
        await _accessCodeRepository.SaveChangesAsync();
        _logger.LogInformation("Access code {CodeId} deleted from album {AlbumId} by {UserId}", codeId, albumGuid, userId);

        return NoContent();
    }

    private async Task<AlbumDetailDto> MapToDetailDtoAsync(Album album)
    {
        return new AlbumDetailDto
        {
            Id = album.Id.ToString(),
            Title = album.Title,
            Description = album.Description ?? string.Empty,
            OwnerId = album.OwnerId,
            CreatedDate = album.CreatedDate,
            CreatedBy = album.CreatedBy,
            CreatedByDisplayName = await _displayNames.ResolveAsync(album.CreatedBy)
        };
    }

    private static string LookupDisplayName(IReadOnlyDictionary<string, string> map, string? key)
    {
        if (!string.IsNullOrWhiteSpace(key) && map.TryGetValue(key, out var name))
        {
            return name;
        }
        return UserDisplayNameResolver.DefaultDisplayName;
    }

    private static string GenerateAccessCode()
    {
        // Generate a 12-character alphanumeric code
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Range(0, 12)
            .Select(_ => chars[random.Next(chars.Length)])
            .ToArray());
    }
}

/// <summary>
/// DTO for album list response
/// </summary>
public class AlbumListDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    /// <summary>Raw uploader user id; kept for clients that need the GUID.</summary>
    public string CreatedBy { get; set; } = string.Empty;
    /// <summary>Resolved display name (e.g. "Phillip Dieppa") for FE rendering.</summary>
    public string CreatedByDisplayName { get; set; } = string.Empty;
    public bool CanManage { get; set; }
}

/// <summary>
/// DTO for album detail response
/// </summary>
public class AlbumDetailDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    /// <summary>Resolved display name (e.g. "Phillip Dieppa") for FE rendering.</summary>
    public string CreatedByDisplayName { get; set; } = string.Empty;
}

/// <summary>
/// DTO for creating an album
/// </summary>
public class CreateAlbumRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// DTO for updating an album
/// </summary>
public class UpdateAlbumRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// DTO for photo list item
/// </summary>
public class PhotoListDto
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? MediumUrl { get; set; }
}

/// <summary>
/// Paginated response envelope for photo lists.
/// </summary>
public class PaginatedPhotosResponse
{
    /// <summary>The current page of photos. Serialized as <c>items</c> per Phase 6 contract.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public List<PhotoListDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>
/// DTO for access code list item
/// </summary>
public class AccessCodeListDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime? ExpirationDate { get; set; }
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    /// <summary>Resolved display name for FE rendering.</summary>
    public string CreatedByDisplayName { get; set; } = string.Empty;
    public bool IsExpired { get; set; }
}

/// <summary>
/// DTO for access code detail response
/// </summary>
public class AccessCodeDetailDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime? ExpirationDate { get; set; }
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    /// <summary>Resolved display name for FE rendering.</summary>
    public string CreatedByDisplayName { get; set; } = string.Empty;
    public bool IsExpired { get; set; }
}

/// <summary>
/// DTO for creating an access code
/// </summary>
public class CreateAccessCodeRequest
{
    public bool ExpiresForever { get; set; } = false;

    /// <summary>
    /// Explicit expiration date (UTC). Takes precedence over ExpirationDays when provided.
    /// </summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>
    /// Number of days from now until expiration. Used as fallback when ExpirationDate is not provided.
    /// </summary>
    public int? ExpirationDays { get; set; } = 30;
}

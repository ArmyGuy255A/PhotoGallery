using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
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
    private readonly IRepository<Photo> _photoRepository;
    private readonly IAccessCodeRepository _accessCodeRepository;
    private readonly ILogger<AlbumsController> _logger;

    public AlbumsController(
        IAlbumRepository albumRepository,
        IRepository<Photo> photoRepository,
        IAccessCodeRepository accessCodeRepository,
        ILogger<AlbumsController> logger)
    {
        _albumRepository = albumRepository;
        _photoRepository = photoRepository;
        _accessCodeRepository = accessCodeRepository;
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

        var result = userAlbums.Select(a => new AlbumListDto
        {
            Id = a.Id.ToString(),
            Title = a.Title,
            Description = a.Description,
            CreatedDate = a.CreatedDate,
            OwnerId = a.OwnerId,
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

        return CreatedAtAction(nameof(GetAlbumById), new { id = album.Id }, MapToDetailDto(album));
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
        return Ok(MapToDetailDto(album));
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
        if (album.OwnerId != userId)
            return Forbid();

        if (!string.IsNullOrWhiteSpace(request.Title))
            album.Title = request.Title;

        if (request.Description != null)
            album.Description = request.Description;

        await _albumRepository.UpdateAsync(album);
        await _albumRepository.SaveChangesAsync();
        _logger.LogInformation("Album {AlbumId} updated by {UserId}", albumId, userId);

        return Ok(MapToDetailDto(album));
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
        if (album.OwnerId != userId)
            return Forbid();

        await _albumRepository.DeleteAsync(album);
        await _albumRepository.SaveChangesAsync();
        _logger.LogInformation("Album {AlbumId} deleted by {UserId}", albumId, userId);

        return NoContent();
    }

    /// <summary>
    /// Get all photos in an album
    /// </summary>
    /// <param name="albumId">Album ID</param>
    /// <returns>List of photos in the album</returns>
    [HttpGet("{albumId}/photos")]
    public async Task<ActionResult<List<PhotoListDto>>> GetAlbumPhotos(string albumId)
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

        var allPhotos = await _photoRepository.GetAllAsync();
        var albumPhotos = allPhotos
            .Where(p => p.AlbumId == albumGuid)
            .Select(p => new PhotoListDto
            {
                Id = p.Id.ToString(),
                FileName = p.FileName,
                UploadDate = p.UploadDate,
                UploadedBy = p.UploadedBy
            })
            .ToList();

        _logger.LogInformation("Retrieved {PhotoCount} photos from album {AlbumId}", albumPhotos.Count, albumGuid);
        return Ok(albumPhotos);
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
        if (album.OwnerId != userId)
            return Forbid();

        var allCodes = await _accessCodeRepository.GetAllAsync();
        var albumCodes = allCodes
            .Where(c => c.AlbumId == albumGuid)
            .Select(c => new AccessCodeListDto
            {
                Id = c.Id.ToString(),
                Code = c.Code,
                ExpirationDate = c.ExpirationDate,
                CreatedDate = c.CreatedDate,
                CreatedBy = c.CreatedBy,
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

        if (album.OwnerId != userId)
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

        // Set expiration date (default 30 days from now, or null if forever)
        DateTime? expirationDate = request.ExpiresForever 
            ? (DateTime?)null 
            : DateTime.UtcNow.AddDays(request.ExpirationDays ?? 30);

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
        if (album.OwnerId != userId)
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

    private AlbumDetailDto MapToDetailDto(Album album)
    {
        return new AlbumDetailDto
        {
            Id = album.Id.ToString(),
            Title = album.Title,
            Description = album.Description,
            OwnerId = album.OwnerId,
            CreatedDate = album.CreatedDate,
            CreatedBy = album.CreatedBy
        };
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
    public bool IsExpired { get; set; }
}

/// <summary>
/// DTO for creating an access code
/// </summary>
public class CreateAccessCodeRequest
{
    public bool ExpiresForever { get; set; } = false;
    public int? ExpirationDays { get; set; } = 30;
}

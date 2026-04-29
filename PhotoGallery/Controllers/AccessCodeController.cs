using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
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
    private readonly IAccessCodeRepository _accessCodeRepository;
    private readonly IRepository<Photo> _photoRepository;
    private readonly IRepository<PhotoVersion> _photoVersionRepository;
    private readonly IRepository<Album> _albumRepository;
    private readonly IStorageProvider _storageProvider;
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<AccessCodeController> _logger;

    public AccessCodeController(
        IAccessCodeRepository accessCodeRepository,
        IRepository<Photo> photoRepository,
        IRepository<PhotoVersion> photoVersionRepository,
        IRepository<Album> albumRepository,
        IStorageProvider storageProvider,
        IImageProcessor imageProcessor,
        ILogger<AccessCodeController> logger)
    {
        _accessCodeRepository = accessCodeRepository;
        _photoRepository = photoRepository;
        _photoVersionRepository = photoVersionRepository;
        _albumRepository = albumRepository;
        _storageProvider = storageProvider;
        _imageProcessor = imageProcessor;
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
    /// Get all photos in an album (requires valid access code)
    /// </summary>
    /// <param name="code">Access code</param>
    /// <returns>List of photos in the album</returns>
    [HttpGet("{code}/photos")]
    public async Task<ActionResult<List<PhotoListItemDto>>> GetAlbumPhotos(string code)
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

        // Get all photos in the album
        var allPhotos = await _photoRepository.GetAllAsync();
        var albumPhotos = allPhotos
            .Where(p => p.AlbumId == accessCode.AlbumId)
            .ToList();

        var result = new List<PhotoListItemDto>();
        foreach (var photo in albumPhotos)
        {
            result.Add(new PhotoListItemDto
            {
                PhotoId = photo.Id.ToString(),
                FileName = photo.FileName,
                UploadDate = photo.UploadDate,
                AvailableQualities = await GetAvailableQualities(photo.Id.ToString())
            });
        }

        _logger.LogInformation("Retrieved {PhotoCount} photos from album {AlbumId} via code", albumPhotos.Count, album.Id);

        return Ok(result);
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
/// DTO for photo list item
/// </summary>
public class PhotoListItemDto
{
    public string PhotoId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    public List<string> AvailableQualities { get; set; } = new();
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Processing;
using PhotoGallery.Services.Storage;
using System.Security.Claims;

namespace PhotoGallery.Controllers;

/// <summary>
/// API endpoints for photo uploads, downloads, and processing
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PhotosController : ControllerBase
{
    private readonly IImageProcessor _imageProcessor;
    private readonly IStorageProvider _storageProvider;
    private readonly IRepository<Photo> _photoRepository;
    private readonly IRepository<Album> _albumRepository;
    private readonly IRepository<PhotoVersion> _photoVersionRepository;
    private readonly ILogger<PhotosController> _logger;

    public PhotosController(
        IImageProcessor imageProcessor,
        IStorageProvider storageProvider,
        IRepository<Photo> photoRepository,
        IRepository<Album> albumRepository,
        IRepository<PhotoVersion> photoVersionRepository,
        ILogger<PhotosController> logger)
    {
        _imageProcessor = imageProcessor;
        _storageProvider = storageProvider;
        _photoRepository = photoRepository;
        _albumRepository = albumRepository;
        _photoVersionRepository = photoVersionRepository;
        _logger = logger;
    }

    /// <summary>
    /// Upload photos to an album (authenticated users only)
    /// </summary>
    /// <param name="albumId">Album ID to upload to</param>
    /// <param name="files">Photo files to upload</param>
    /// <returns>List of job IDs for uploaded photos</returns>
    [Authorize]
    [HttpPost("albums/{albumId}")]
    public async Task<ActionResult<UploadPhotoResponse>> UploadPhotos(string albumId, IFormFileCollection files)
    {
        if (!Guid.TryParse(albumId, out var albumGuid))
            return BadRequest("Invalid album ID");

        if (files.Count == 0)
            return BadRequest("No files provided");

        var album = await _albumRepository.GetByIdAsync(albumGuid);
        if (album == null)
            return NotFound("Album not found");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        // Verify user is album owner or admin
        if (album.OwnerId != userId && !User.IsInRole("Admin"))
            return Forbid();

        var uploadedPhotos = new List<PhotoUploadInfo>();
        var errors = new List<string>();

        foreach (var file in files)
        {
            try
            {
                if (file.Length == 0)
                {
                    errors.Add($"{file.FileName}: File is empty");
                    continue;
                }

                // Allowed image formats
                var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/webp" };
                if (!allowedMimeTypes.Contains(file.ContentType))
                {
                    errors.Add($"{file.FileName}: Invalid file type. Only JPEG, PNG, and WebP are allowed");
                    continue;
                }

                // Create photo entity
                var photo = new Photo
                {
                    Id = Guid.NewGuid(),
                    AlbumId = albumGuid,
                    FileName = file.FileName,
                    UploadDate = DateTime.UtcNow,
                    UploadedBy = userId,
                    StorageKey = $"photos/{albumGuid}/{Guid.NewGuid()}/{file.FileName}"
                };

                // Upload original file to storage
                using (var stream = file.OpenReadStream())
                {
                    await _storageProvider.UploadAsync(photo.StorageKey, stream, file.ContentType);
                }

                // Save photo to database
                await _photoRepository.AddAsync(photo);

                // Queue for processing
                var jobId = await _imageProcessor.QueuePhotoAsync(photo.Id.ToString());

                uploadedPhotos.Add(new PhotoUploadInfo
                {
                    PhotoId = photo.Id.ToString(),
                    FileName = photo.FileName,
                    ProcessingJobId = jobId
                });

                _logger.LogInformation(
                    "Photo {PhotoFileName} uploaded to album {AlbumId} by {UserId}",
                    file.FileName, albumGuid, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading photo {FileName}", file.FileName);
                errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        return Ok(new UploadPhotoResponse
        {
            SuccessfulUploads = uploadedPhotos,
            Errors = errors,
            TotalUploaded = uploadedPhotos.Count,
            TotalFailed = errors.Count
        });
    }

    /// <summary>
    /// Download a photo by quality level (authenticated users only)
    /// </summary>
    /// <param name="photoId">Photo ID</param>
    /// <param name="quality">Quality level: high, medium, low, or raw</param>
    /// <returns>Photo file stream</returns>
    [Authorize]
    [HttpGet("{photoId}/download")]
    public async Task<ActionResult> DownloadPhotoAuthenticated(string photoId, [FromQuery] string quality = "medium")
    {
        if (!Guid.TryParse(photoId, out var photoGuid))
            return BadRequest("Invalid photo ID");

        var photo = await _photoRepository.GetByIdAsync(photoGuid);
        if (photo == null)
            return NotFound("Photo not found");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        // Verify user has access to the album
        var album = await _albumRepository.GetByIdAsync(photo.AlbumId);
        if (album == null)
            return NotFound("Album not found");

        // Check user access (owner or admin)
        if (album.OwnerId != userId && !User.IsInRole("Admin"))
            return Forbid();

        var photoVersion = await _imageProcessor.GetPhotoVersionAsync(photoId, quality);
        if (photoVersion == null)
            return NotFound($"Photo version not found for quality: {quality}");

        try
        {
            var stream = await _storageProvider.DownloadAsync(photoVersion.StorageKey);
            _logger.LogInformation(
                "Photo {PhotoId} downloaded at {Quality} quality by {UserId}",
                photoId, quality, userId);

            return File(stream, "image/jpeg", $"{photo.FileName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading photo {PhotoId}", photoId);
            return StatusCode(500, "Error downloading photo");
        }
    }

    /// <summary>
    /// Get available compression profiles
    /// </summary>
    /// <returns>List of compression profiles</returns>
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

    /// <summary>
    /// Get processing status for a photo
    /// </summary>
    /// <param name="jobId">Processing job ID</param>
    /// <returns>Processing status</returns>
    [Authorize]
    [HttpGet("processing-status/{jobId}")]
    public async Task<ActionResult<ProcessingStatusDto>> GetProcessingStatus(string jobId)
    {
        // This endpoint would fetch from ProcessingQueue in a real implementation
        // For now, we return a placeholder
        return Ok(new ProcessingStatusDto
        {
            JobId = jobId,
            Status = "Processing",
            CompletedVersions = 0,
            TotalVersions = 4
        });
    }
}

/// <summary>
/// Response DTO for photo upload
/// </summary>
public class UploadPhotoResponse
{
    public List<PhotoUploadInfo> SuccessfulUploads { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public int TotalUploaded { get; set; }
    public int TotalFailed { get; set; }
}

/// <summary>
/// DTO for individual photo upload info
/// </summary>
public class PhotoUploadInfo
{
    public string PhotoId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ProcessingJobId { get; set; } = string.Empty;
}

/// <summary>
/// DTO for compression profile
/// </summary>
public class CompressionProfileDto
{
    public string Name { get; set; } = string.Empty;
    public int QualityPercentage { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// DTO for processing status
/// </summary>
public class ProcessingStatusDto
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int CompletedVersions { get; set; }
    public int TotalVersions { get; set; }
}

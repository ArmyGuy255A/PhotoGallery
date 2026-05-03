using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Enums;
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
    private readonly IProcessingQueueItemRepository _queueItemRepository;
    private readonly ILogger<PhotosController> _logger;

    public PhotosController(
        IImageProcessor imageProcessor,
        IStorageProvider storageProvider,
        IRepository<Photo> photoRepository,
        IRepository<Album> albumRepository,
        IRepository<PhotoVersion> photoVersionRepository,
        IProcessingQueueItemRepository queueItemRepository,
        ILogger<PhotosController> logger)
    {
        _imageProcessor = imageProcessor;
        _storageProvider = storageProvider;
        _photoRepository = photoRepository;
        _albumRepository = albumRepository;
        _photoVersionRepository = photoVersionRepository;
        _queueItemRepository = queueItemRepository;
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
                var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/webp", "application/octet-stream" };
                if (!allowedMimeTypes.Contains(file.ContentType))
                {
                    errors.Add($"{file.FileName}: Invalid file type '{file.ContentType}'. Only JPEG, PNG, and WebP are allowed");
                    _logger.LogWarning("Rejected file {FileName} with content type {ContentType}", file.FileName, file.ContentType);
                    continue;
                }

                // Create photo entity
                var photoId = Guid.NewGuid();
                var photo = new Photo
                {
                    Id = photoId,
                    AlbumId = albumGuid,
                    FileName = file.FileName,
                    UploadDate = DateTime.UtcNow,
                    UploadedBy = userId,
                    StorageKey = $"photogallery/{albumGuid}/{photoId}/original.jpg"
                };

                _logger.LogInformation("Starting upload for photo {PhotoId} (file: {FileName}, size: {FileSize})", photoId, file.FileName, file.Length);

                // Upload original file to storage
                try
                {
                    using (var stream = file.OpenReadStream())
                    {
                        _logger.LogInformation("Stream opened for {FileName}, uploading to {StorageKey}", file.FileName, photo.StorageKey);
                        await _storageProvider.UploadAsync(photo.StorageKey, stream, file.ContentType);
                        _logger.LogInformation("File {StorageKey} uploaded successfully to storage", photo.StorageKey);
                    }
                }
                catch (Exception storageEx)
                {
                    _logger.LogError(storageEx, "Storage provider error uploading {StorageKey}", photo.StorageKey);
                    errors.Add($"{file.FileName}: Storage error - {storageEx.Message}");
                    continue;
                }

                // Save photo to database
                try
                {
                    await _photoRepository.AddAsync(photo);
                    await _photoRepository.SaveChangesAsync();
                    _logger.LogInformation("Photo {PhotoId} saved to database", photoId);
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "Database error saving photo {PhotoId}", photoId);
                    errors.Add($"{file.FileName}: Database error - {dbEx.Message}");
                    continue;
                }

                // Queue for processing
                try
                {
                    var jobId = await _imageProcessor.QueuePhotoAsync(photo.Id.ToString());
                    _logger.LogInformation("Photo {PhotoId} queued for processing with job {JobId}", photoId, jobId);

                    uploadedPhotos.Add(new PhotoUploadInfo
                    {
                        PhotoId = photo.Id.ToString(),
                        FileName = photo.FileName,
                        ProcessingJobId = jobId
                    });
                }
                catch (Exception processingEx)
                {
                    _logger.LogError(processingEx, "Error queuing photo {PhotoId} for processing", photoId);
                    // Don't fail the upload if queuing fails - photo is already in storage and DB
                }

                _logger.LogInformation(
                    "Photo {PhotoFileName} uploaded to album {AlbumId} by {UserId}",
                    file.FileName, albumGuid, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error uploading photo {FileName}: {ExceptionMessage}", file.FileName, ex.Message);
                errors.Add($"{file.FileName}: {ex.GetType().Name} - {ex.Message}");
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
    /// <param name="photoId">Photo ID</param>
    /// <returns>Processing status with version completeness</returns>
    [Authorize]
    [HttpGet("{photoId}/status")]
    public async Task<ActionResult<ProcessingStatusDto>> GetProcessingStatus(string photoId)
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

        if (album.OwnerId != userId && !User.IsInRole("Admin"))
            return Forbid();

        // Get all processing queue items for this photo
        var allItems = await _queueItemRepository.GetAllAsync();
        var photoItems = allItems.Where(i => i.PhotoId == photoGuid).ToList();
        
        // Count completed versions
        var completedItems = photoItems.Where(i => i.Status == ProcessingStatus.Complete).ToList();
        var hasThumbnail = completedItems.Any(i => i.Quality == QualityType.Thumbnail);
        var hasLow = completedItems.Any(i => i.Quality == QualityType.Low);
        var hasMedium = completedItems.Any(i => i.Quality == QualityType.Medium);
        var hasHigh = completedItems.Any(i => i.Quality == QualityType.High);
        
        var completedVersions = (hasThumbnail ? 1 : 0) + (hasLow ? 1 : 0) + (hasMedium ? 1 : 0) + (hasHigh ? 1 : 0);
        var totalVersions = 4;
        var percentComplete = totalVersions > 0 ? (completedVersions * 100) / totalVersions : 0;
        var status = completedVersions == totalVersions ? "Complete" : "Processing";

        return Ok(new ProcessingStatusDto
        {
            PhotoId = photoId,
            Status = status,
            CompletedVersions = completedVersions,
            TotalVersions = totalVersions,
            PercentComplete = percentComplete,
            ProcessingStartedAt = photo.ProcessingStartedAt,
            ProcessingCompletedAt = completedVersions == totalVersions ? DateTime.UtcNow : null,
            HasThumbnail = hasThumbnail,
            HasLow = hasLow,
            HasMedium = hasMedium,
            HasHigh = hasHigh
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
    public string PhotoId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int CompletedVersions { get; set; }
    public int TotalVersions { get; set; }
    public int PercentComplete { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessingCompletedAt { get; set; }
    public bool HasThumbnail { get; set; }
    public bool HasLow { get; set; }
    public bool HasMedium { get; set; }
    public bool HasHigh { get; set; }
}

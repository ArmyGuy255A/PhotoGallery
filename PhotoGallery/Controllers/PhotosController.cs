using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Enums;
using PhotoGallery.Hubs;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Processing;
using PhotoGallery.Services.Storage;
using System.Security.Claims;

namespace PhotoGallery.Controllers;

/// <summary>
/// API endpoints for photo uploads, downloads, and processing.
///
/// Upload paths — there are two:
///
///   1. Direct-to-blob (Phase 2, preferred):
///      <c>POST /api/photos/albums/{albumId}/upload-tickets</c> mints write
///      SAS URLs; the SPA PUTs the files directly to storage; then
///      <c>POST /api/photos/{photoId}/upload-complete</c> verifies the blob
///      landed, transitions the row to <c>Pending</c>, creates the per-quality
///      <c>ProcessingQueueItem</c>s, and broadcasts <c>ProcessingStarted</c>
///      via the SignalR hub. The API container never sees the file bytes —
///      uploads are bounded by SPA ↔ storage bandwidth, not by the container
///      CPU/network budget.
///
///   2. Server-proxied multipart (<c>POST /api/photos/albums/{albumId}</c>):
///      kept as a fallback for tests, very small uploads, and any client
///      that can't speak the 3-call flow. The two paths converge at the
///      same database + queue rows.
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
    private readonly IRepository<ProcessingQueue> _queueRepository;
    private readonly IProcessingQueueItemRepository _queueItemRepository;
    private readonly StorageConsistencyService _storageConsistencyService;
    private readonly IHubContext<PhotoProgressHub> _progressHub;
    private readonly ILogger<PhotosController> _logger;

    // Single-file upload ceiling for direct-to-blob tickets. Picked to match
    // the largest practical RAW/JPEG photo without inviting abuse. Kestrel's
    // default request limit (30 MB) only matters for the multipart fallback
    // path; SAS-uploaded blobs never traverse the API host.
    private const long MaxUploadBytes = 100L * 1024 * 1024;

    // TTL for the write-only SAS minted by /upload-tickets. Long enough for
    // a flaky mobile uplink to finish the PUT, short enough that an
    // abandoned ticket expires before the orphan-blob reaper (Phase 5,
    // default 60 min grace) considers reaping it.
    private static readonly TimeSpan UploadSasTtl = TimeSpan.FromMinutes(30);

    public PhotosController(
        IImageProcessor imageProcessor,
        IStorageProvider storageProvider,
        IRepository<Photo> photoRepository,
        IRepository<Album> albumRepository,
        IRepository<PhotoVersion> photoVersionRepository,
        IRepository<ProcessingQueue> queueRepository,
        IProcessingQueueItemRepository queueItemRepository,
        StorageConsistencyService storageConsistencyService,
        IHubContext<PhotoProgressHub> progressHub,
        ILogger<PhotosController> logger)
    {
        _imageProcessor = imageProcessor;
        _storageProvider = storageProvider;
        _photoRepository = photoRepository;
        _albumRepository = albumRepository;
        _photoVersionRepository = photoVersionRepository;
        _queueRepository = queueRepository;
        _queueItemRepository = queueItemRepository;
        _storageConsistencyService = storageConsistencyService;
        _progressHub = progressHub;
        _logger = logger;
    }

    /// <summary>
    /// Direct-to-blob upload, step 1: mint per-file write SAS URLs.
    ///
    /// For every requested file, validates ownership of the target album,
    /// rejects oversize requests, inserts a Photo row in
    /// <see cref="PhotoProcessingStatus.Uploading"/> (so the album listing
    /// keeps hiding the row until step 2 lands), inserts a parent
    /// <see cref="ProcessingQueue"/> bound to the photo, and returns the
    /// write SAS that the SPA will PUT to.
    ///
    /// The per-quality <see cref="ProcessingQueueItem"/> rows are deliberately
    /// NOT created here — they're created in <see cref="UploadComplete"/>
    /// once the blob actually exists. This guarantees the worker only ever
    /// dequeues photos whose original.jpg is real.
    /// </summary>
    [Authorize]
    [HttpPost("albums/{albumId}/upload-tickets")]
    public async Task<ActionResult<List<UploadTicketResponse>>> CreateUploadTickets(
        string albumId,
        [FromBody] List<UploadTicketRequest> files)
    {
        if (!Guid.TryParse(albumId, out var albumGuid))
            return BadRequest("Invalid album ID");

        if (files == null || files.Count == 0)
            return BadRequest("No files requested");

        var album = await _albumRepository.GetByIdAsync(albumGuid);
        if (album == null)
            return NotFound("Album not found");

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (album.OwnerId != userId && !User.IsInRole("Admin"))
            return Forbid();

        var responses = new List<UploadTicketResponse>(files.Count);

        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.FileName))
                return BadRequest("fileName is required for every ticket");

            if (file.Size <= 0 || file.Size > MaxUploadBytes)
                return BadRequest(
                    $"{file.FileName}: size {file.Size} bytes is outside the allowed range " +
                    $"(0, {MaxUploadBytes}]");

            var photoId = Guid.NewGuid();
            var blobPath = $"photogallery/{albumGuid}/{photoId}/original.jpg";

            var photo = new Photo
            {
                Id = photoId,
                AlbumId = albumGuid,
                FileName = file.FileName,
                UploadDate = DateTime.UtcNow,
                UploadedBy = userId,
                StorageKey = blobPath,
                ProcessingStatus = PhotoProcessingStatus.Uploading
            };
            await _photoRepository.AddAsync(photo);

            var queue = new ProcessingQueue
            {
                PhotoId = photoId,
                Status = ProcessingStatus.Pending
            };
            await _queueRepository.AddAsync(queue);

            // Save before minting the SAS so that, if save fails, we don't
            // hand the SPA a writable URL into storage with no row to
            // reconcile against.
            await _photoRepository.SaveChangesAsync();

            var uploadUrl = await _storageProvider.GenerateWriteSasUrlAsync(blobPath, UploadSasTtl);
            var expiresAt = DateTime.UtcNow.Add(UploadSasTtl);

            responses.Add(new UploadTicketResponse
            {
                PhotoId = photoId.ToString(),
                UploadUrl = uploadUrl,
                BlobPath = blobPath,
                ExpiresAt = expiresAt
            });

            _logger.LogInformation(
                "Issued upload ticket for photo {PhotoId} (file {FileName}, {Size} bytes) in album {AlbumId} for user {UserId}",
                photoId, file.FileName, file.Size, albumGuid, userId);
        }

        return Ok(responses);
    }

    /// <summary>
    /// Direct-to-blob upload, step 2: confirm the PUT landed and start
    /// processing.
    ///
    /// Verifies the blob exists at the expected path (HEAD via
    /// <c>BlobClient.ExistsAsync</c> through <see cref="IStorageProvider.ExistsAsync"/>),
    /// transitions the row from <c>Uploading</c> to <c>Pending</c>, creates
    /// the per-quality <see cref="ProcessingQueueItem"/> rows that the worker
    /// will pick up, and broadcasts <c>ProcessingStarted</c> to the
    /// uploader's hub group.
    /// </summary>
    [Authorize]
    [HttpPost("{photoId:guid}/upload-complete")]
    public async Task<ActionResult<UploadCompleteResponse>> UploadComplete(
        Guid photoId,
        [FromBody] UploadCompleteRequest body)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var photo = await _photoRepository.GetByIdAsync(photoId);
        if (photo == null)
            return NotFound("Photo not found");

        if (photo.UploadedBy != userId && !User.IsInRole("Admin"))
            return Forbid();

        if (photo.ProcessingStatus != PhotoProcessingStatus.Uploading)
        {
            // Idempotency: a duplicate /upload-complete after the row already
            // transitioned to Pending/Processing/Complete should be a no-op,
            // not an error. The SPA may retry the call on a flaky network.
            _logger.LogInformation(
                "Photo {PhotoId} upload-complete called but status is already {Status} — treating as no-op",
                photoId, photo.ProcessingStatus);
            return Ok(new UploadCompleteResponse
            {
                PhotoId = photoId.ToString(),
                Status = photo.ProcessingStatus.ToString()
            });
        }

        // HEAD the blob — never trust the client's claim that the PUT landed.
        // If the blob isn't there, the row stays in Uploading and the
        // orphan-blob reaper (Phase 5) will eventually delete the photo row.
        if (!await _storageProvider.ExistsAsync(photo.StorageKey))
        {
            _logger.LogWarning(
                "upload-complete for photo {PhotoId} but blob {Key} does not exist — refusing",
                photoId, photo.StorageKey);
            return BadRequest(new { error = "Blob not found at expected path", blobPath = photo.StorageKey });
        }

        // Locate (or create) the parent ProcessingQueue row that
        // /upload-tickets inserted, and add the four quality items.
        var allQueues = await _queueRepository.GetAllAsync();
        var queue = allQueues.FirstOrDefault(q => q.PhotoId == photoId);
        if (queue == null)
        {
            queue = new ProcessingQueue { PhotoId = photoId, Status = ProcessingStatus.Pending };
            await _queueRepository.AddAsync(queue);
            await _queueRepository.SaveChangesAsync();
        }

        var qualities = new[]
        {
            QualityType.Thumbnail, QualityType.Low, QualityType.Medium, QualityType.High
        };
        foreach (var quality in qualities)
        {
            await _queueItemRepository.AddAsync(new ProcessingQueueItem
            {
                ProcessingQueueId = queue.Id,
                PhotoId = photoId,
                Quality = quality,
                Status = ProcessingStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });
        }

        photo.ProcessingStatus = PhotoProcessingStatus.Pending;
        photo.ProcessingStartedAt = DateTime.UtcNow;
        await _photoRepository.UpdateAsync(photo);
        await _photoRepository.SaveChangesAsync();
        await _queueItemRepository.SaveChangesAsync();

        // Broadcast ProcessingStarted (no quality — this is the per-photo
        // event) to the uploader's hub group. Each per-quality
        // ProcessingStarted event is emitted by ImageProcessingService when
        // the worker actually picks up the queue item.
        try
        {
            await _progressHub.Clients
                .Group(PhotoProgressHub.UserGroup(photo.UploadedBy))
                .SendAsync(
                    PhotoProgressEvents.ProcessingStarted,
                    new ProcessingStartedPayload(photoId.ToString(), null));
        }
        catch (Exception ex)
        {
            // Hub broadcast must never block the upload-complete response.
            // The SPA will catch up via PhotoProgressHub.RequestStatus on
            // its next (re)connect.
            _logger.LogWarning(ex,
                "Failed to broadcast ProcessingStarted for photo {PhotoId} — non-fatal",
                photoId);
        }

        _logger.LogInformation(
            "Photo {PhotoId} upload-complete: queued 4 quality items, transitioned to Pending. ActualSize={ActualSize} bytes Checksum={Checksum}",
            photoId, body.ActualSize, body.Checksum ?? "(none)");

        return Ok(new UploadCompleteResponse
        {
            PhotoId = photoId.ToString(),
            Status = PhotoProcessingStatus.Pending.ToString()
        });
    }

    /// <summary>
    /// Upload photos to an album (multipart fallback path).
    ///
    /// Kept for tests, small uploads, and clients that can't speak the
    /// direct-to-blob flow above. The preferred path is
    /// <see cref="CreateUploadTickets"/> + <see cref="UploadComplete"/>.
    /// </summary>
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

    /// <summary>
    /// Delete a single photo (owner or admin only).
    /// Removes every storage object under <c>photogallery/{albumId}/{photoId}/</c>
    /// (originals, every quality variant, watermarked variants) and then deletes
    /// the <see cref="Photo"/> row. EF Core cascades clean up the related
    /// <c>PhotoVersion</c>, <c>PhotoFile</c>, <c>PhotoVersionUrl</c>,
    /// <c>ProcessingQueueItem</c>, <c>Download</c>, and <c>UserCartItem</c>
    /// rows (configured in <c>PhotoConfiguration</c> / <c>DownloadConfiguration</c> /
    /// <c>UserCartItemConfiguration</c>).
    ///
    /// Reference: issue #113.
    /// </summary>
    [Authorize]
    [HttpDelete("{photoId:guid}")]
    public async Task<IActionResult> DeletePhoto(Guid photoId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var photo = await _photoRepository.GetByIdAsync(photoId);
        if (photo == null)
            return NotFound();

        var album = await _albumRepository.GetByIdAsync(photo.AlbumId);
        if (album == null)
            return NotFound();

        // Owner or Admin only. Saved-access-code holders cannot delete.
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin && album.OwnerId != userId)
            return Forbid();

        // Best-effort storage cleanup. We collect failures but never block the
        // DB delete on them — orphan blobs are picked up by
        // StorageConsistencyService on its next sweep.
        var prefix = $"photogallery/{photo.AlbumId}/{photoId}/";
        try
        {
            var keys = await _storageProvider.ListAsync(prefix);
            foreach (var key in keys)
            {
                try { await _storageProvider.DeleteAsync(key); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to delete storage object {Key} while deleting photo {PhotoId} (continuing)",
                        key, photoId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to list storage objects under {Prefix} while deleting photo {PhotoId} (continuing — orphans will be picked up by the consistency sweep)",
                prefix, photoId);
        }

        await _photoRepository.DeleteAsync(photo);
        await _photoRepository.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} (admin={IsAdmin}) deleted photo {PhotoId} from album {AlbumId}",
            userId, isAdmin, photoId, photo.AlbumId);

        return NoContent();
    }

    /// <summary>
    /// Admin-only: synchronously trigger a storage/DB consistency reconciliation cycle (D007).
    /// Returns the per-cycle summary report once reconciliation completes.
    ///
    /// Synchronous by design for v1 — admins are technical users with no SLA, datasets
    /// are small in practice, and a sync endpoint avoids the complexity of a separate
    /// job-status table. Future v2 may move to 202 Accepted + job-id polling.
    ///
    /// Reference: D007 (Storage/Database Consistency Reconciliation), D001 (Auth model).
    /// </summary>
    [HttpPost("admin/reconcile-storage")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ReconcileStorage(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Admin {User} triggered storage reconciliation", User.Identity?.Name);
            var report = await _storageConsistencyService.RunOnceAsync(cancellationToken);
            return Ok(report);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Admin storage reconciliation was cancelled");
            return StatusCode(499, new { message = "Reconciliation cancelled by client" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin storage reconciliation failed");
            return StatusCode(500, new { message = "Reconciliation failed", error = ex.Message });
        }
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

/// <summary>
/// Per-file request entry for <c>POST /api/photos/albums/{id}/upload-tickets</c>.
/// </summary>
public class UploadTicketRequest
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
}

/// <summary>
/// Per-file response entry for <c>POST /api/photos/albums/{id}/upload-tickets</c>.
/// The SPA PUTs the file bytes to <see cref="UploadUrl"/> and then calls
/// <c>POST /api/photos/{photoId}/upload-complete</c>.
/// </summary>
public class UploadTicketResponse
{
    public string PhotoId { get; set; } = string.Empty;
    public string UploadUrl { get; set; } = string.Empty;
    public string BlobPath { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Body for <c>POST /api/photos/{photoId}/upload-complete</c>. The optional
/// checksum is logged for forensics; we don't verify it against the blob
/// today (Azure Blob returns Content-MD5 if the client sent it on the PUT).
/// </summary>
public class UploadCompleteRequest
{
    public long ActualSize { get; set; }
    public string? Checksum { get; set; }
}

public class UploadCompleteResponse
{
    public string PhotoId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

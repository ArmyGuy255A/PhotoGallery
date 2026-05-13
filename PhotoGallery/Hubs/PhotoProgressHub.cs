using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Hubs;

/// <summary>
/// SignalR hub that pushes per-photo processing progress to the uploader
/// in real time (Phase 3 of the v2 plan).
///
/// Connection model
/// ----------------
/// Browsers cannot set custom headers on the WebSocket upgrade request, so
/// the SPA carries the app JWT in the <c>?access_token=...</c> query string.
/// The JwtBearer scheme is wired in <c>Authentication/DependencyInjection.cs</c>
/// to read that query parameter for paths under <c>/hubs/</c>. The hub itself
/// is therefore authenticated identically to a controller.
///
/// Group naming: every connection joins <c>user:{nameid}</c> on connect, so
/// broadcasts to a specific uploader are addressed via
/// <c>Clients.Group($"user:{photo.UploadedBy}")</c>. This keeps progress
/// events private to the user who owns the photos.
/// </summary>
[Authorize]
public class PhotoProgressHub : Hub
{
    private readonly IProcessingQueueItemRepository _queueItemRepository;
    private readonly IRepository<Photo> _photoRepository;
    private readonly ILogger<PhotoProgressHub> _logger;

    public PhotoProgressHub(
        IProcessingQueueItemRepository queueItemRepository,
        IRepository<Photo> photoRepository,
        ILogger<PhotoProgressHub> logger)
    {
        _queueItemRepository = queueItemRepository;
        _photoRepository = photoRepository;
        _logger = logger;
    }

    /// <summary>
    /// Build the per-user group name. Exposed as a helper so broadcasts from
    /// <c>ImageProcessingService</c> / <c>PhotosController</c> and the hub
    /// itself agree on the string format.
    /// </summary>
    public static string UserGroup(string userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning(
                "PhotoProgressHub connection {ConnectionId} has no NameIdentifier — aborting.",
                Context.ConnectionId);
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        _logger.LogInformation(
            "PhotoProgressHub connection {ConnectionId} joined group {Group}",
            Context.ConnectionId, UserGroup(userId));
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Server-callable method: return a snapshot of the current processing
    /// queue items for one photo to the calling connection only. Used by the
    /// SPA after a reconnect to catch up on events it missed during the
    /// disconnect window.
    /// </summary>
    public async Task RequestStatus(string photoId)
    {
        if (!Guid.TryParse(photoId, out var photoGuid))
        {
            await Clients.Caller.SendAsync("ProcessingError",
                new { photoId, error = "Invalid photo id" });
            return;
        }

        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        var photo = await _photoRepository.GetByIdAsync(photoGuid);
        if (photo == null)
        {
            await Clients.Caller.SendAsync("ProcessingError",
                new { photoId, error = "Photo not found" });
            return;
        }

        // Ownership check — never let a connection peek at another user's
        // processing state. Admins are treated the same as regular users
        // here; the hub is a per-user push channel, not an admin dashboard.
        if (photo.UploadedBy != userId)
        {
            _logger.LogWarning(
                "User {UserId} requested status for photo {PhotoId} they do not own — refusing",
                userId, photoGuid);
            return;
        }

        var items = (await _queueItemRepository.GetByPhotoIdAsync(photoGuid))
            .Select(i => new
            {
                quality = i.Quality.ToString(),
                status = i.Status.ToString(),
                retryCount = i.RetryCount,
                lastError = i.LastError
            })
            .ToArray();

        await Clients.Caller.SendAsync("StatusSnapshot", new
        {
            photoId = photoGuid.ToString(),
            processingStatus = photo.ProcessingStatus.ToString(),
            items
        });
    }
}

/// <summary>
/// Hub-method names broadcast to the user's group. Kept as constants so the
/// producer side (controller + ImageProcessingService) and any future
/// strongly-typed client share a single source of truth.
/// </summary>
public static class PhotoProgressEvents
{
    public const string ProcessingStarted = nameof(ProcessingStarted);
    public const string ProcessingProgress = nameof(ProcessingProgress);
    public const string ProcessingCompleted = nameof(ProcessingCompleted);
}

public record ProcessingStartedPayload(string PhotoId, string? Quality);
public record ProcessingProgressPayload(string PhotoId, string Quality, int Percent);
public record ProcessingCompletedPayload(string PhotoId, string Quality, bool Success, string? BlobPath, string? Error);

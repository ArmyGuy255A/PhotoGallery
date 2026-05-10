using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Storage;

namespace PhotoGallery.Services;

/// <summary>
/// Audited admin operation: regenerate the watermarked variants for a scope (album, or all
/// albums) by deleting the existing <c>*-watermarked.jpg</c> blobs from object storage. The
/// next public/guest request for each affected variant triggers the inline backfill in
/// <see cref="PhotoVersionUrlService.TryGenerateWatermarkedVariantAsync"/>, which now uses
/// the corrected display-name resolution (Users table → "© First Last" / email-local /
/// "© Photo Gallery"). Reference: TODO at <c>ImageProcessingService.cs:290</c> + PRs #47 / #48.
///
/// Why delete-and-let-on-demand-rebuild instead of regenerating eagerly:
///  - Reuses the on-demand backfill code path that PR #48 already shipped + tested.
///  - Spreads the regeneration cost across actual viewer traffic instead of a one-shot job
///    that could overwhelm storage or saturate CPU on the worker.
///  - Idempotent: re-running is safe; rows that have no watermarked blob are no-ops.
///
/// Every invocation is recorded in the AuditLogEntry table with the actor's email, the
/// reason supplied by the operator, and counts of blobs deleted, so prod regenerations
/// are traceable.
/// </summary>
public interface IWatermarkBackfillService
{
    Task<WatermarkBackfillResult> RegenerateAsync(
        WatermarkBackfillRequest request,
        string actorUserId,
        string actorEmail,
        CancellationToken ct = default);
}

/// <summary>
/// Operator-supplied request for a watermark regeneration sweep. <c>Reason</c> is mandatory
/// (audit-logged); <c>AlbumId</c> is optional — null means "all albums".
/// </summary>
public record WatermarkBackfillRequest(
    Guid? AlbumId,
    string Reason);

public record WatermarkBackfillResult(
    int PhotosScanned,
    int BlobsDeleted,
    int BlobsMissing,
    int Errors);

public class WatermarkBackfillService : IWatermarkBackfillService
{
    private static readonly QualityType[] WatermarkedQualities = new[]
    {
        QualityType.Thumbnail,
        QualityType.Medium,
    };

    private readonly IPhotoRepository _photoRepository;
    private readonly IStorageProvider _storage;
    private readonly IAuditLogRepository _auditLog;
    private readonly ILogger<WatermarkBackfillService> _logger;

    public WatermarkBackfillService(
        IPhotoRepository photoRepository,
        IStorageProvider storage,
        IAuditLogRepository auditLog,
        ILogger<WatermarkBackfillService> logger)
    {
        _photoRepository = photoRepository;
        _storage = storage;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<WatermarkBackfillResult> RegenerateAsync(
        WatermarkBackfillRequest request,
        string actorUserId,
        string actorEmail,
        CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("Reason is required for an audited regenerate sweep.", nameof(request));
        }

        var photos = request.AlbumId is { } albumId
            ? await _photoRepository.GetAlbumPhotosAsync(albumId)
            : (await _photoRepository.GetAllAsync()).ToList();

        var scanned = 0;
        var deleted = 0;
        var missing = 0;
        var errors = 0;

        foreach (var photo in photos)
        {
            ct.ThrowIfCancellationRequested();
            scanned++;

            foreach (var quality in WatermarkedQualities)
            {
                var key = PhotoVersionUrlService.BuildWatermarkedStorageKey(photo.AlbumId, photo.Id, quality);
                try
                {
                    var existed = await _storage.DeleteAsync(key);
                    if (existed) deleted++;
                    else missing++;
                }
                catch (Exception ex)
                {
                    errors++;
                    _logger.LogWarning(ex,
                        "Failed to delete watermarked variant {Key} during regenerate sweep",
                        key);
                }
            }
        }

        var result = new WatermarkBackfillResult(scanned, deleted, missing, errors);

        await _auditLog.AddEntryAsync(new AuditLogEntry
        {
            ActorUserId = actorUserId,
            ActorEmail = actorEmail,
            Action = "watermark.regenerate",
            TargetType = request.AlbumId.HasValue ? "Album" : "All",
            TargetId = request.AlbumId?.ToString(),
            Details = System.Text.Json.JsonSerializer.Serialize(new
            {
                reason = request.Reason,
                photosScanned = result.PhotosScanned,
                blobsDeleted = result.BlobsDeleted,
                blobsMissing = result.BlobsMissing,
                errors = result.Errors,
            }),
        });

        _logger.LogInformation(
            "Watermark regenerate sweep by {Actor}: scope={Scope}, scanned={Scanned}, deleted={Deleted}, missing={Missing}, errors={Errors}, reason={Reason}",
            actorEmail,
            request.AlbumId?.ToString() ?? "all",
            result.PhotosScanned,
            result.BlobsDeleted,
            result.BlobsMissing,
            result.Errors,
            request.Reason);

        return result;
    }
}

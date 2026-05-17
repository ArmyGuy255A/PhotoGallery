using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Models;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Hard-deletes Photo rows that the reconciler has marked Failed. Used to
/// clean up DB orphans (Photo rows whose original blob is gone — e.g. after
/// chaos or after a partial upload failure).
///
/// Cascade is NOT configured on every Photo-referencing FK
/// (ProcessingQueues.PhotoId is RESTRICT, for example), so we explicitly
/// drop each dependent set first inside one SaveChanges per batch:
///
///   1. ProcessingQueueItems  (-> Photos via PhotoId; also -> ProcessingQueueId)
///   2. ProcessingQueues      (-> Photos via PhotoId)
///   3. PhotoVersions         (-> Photos)
///   4. PhotoVersionUrls      (-> Photos)
///   5. PhotoFiles            (-> Photos)
///   6. UserCartItems         (-> Photos)
///   7. Downloads             (-> Photos)
///   8. Photos                (the row itself)
///
/// Order matters because ProcessingQueueItem has FKs to BOTH Photo and
/// ProcessingQueue — drop the items first, then the parent queue.
/// </summary>
public class FailedPhotoPurgeService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FailedPhotoPurgeService> _logger;

    public FailedPhotoPurgeService(IServiceProvider serviceProvider, ILogger<FailedPhotoPurgeService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<FailedPhotoPurgeReport> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var failed = await db.Photos
            .Where(p => p.ProcessingStatus == PhotoProcessingStatus.Failed)
            .Select(p => new { p.Id, p.AlbumId, p.FileName })
            .ToListAsync(cancellationToken);

        if (failed.Count == 0)
        {
            _logger.LogInformation("FailedPhotoPurgeService: no Failed photos to purge");
            return new FailedPhotoPurgeReport { PhotosScanned = 0, PhotosDeleted = 0 };
        }

        _logger.LogWarning(
            "FailedPhotoPurgeService: hard-deleting {Count} Failed Photo rows across {Albums} album(s)",
            failed.Count, failed.Select(f => f.AlbumId).Distinct().Count());

        var totalDeleted = 0;
        const int BatchSize = 500;
        for (var offset = 0; offset < failed.Count; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchIds = failed.Skip(offset).Take(BatchSize).Select(f => f.Id).ToList();

            // Order matters — drop the leaf tables first so the FK chain to
            // Photos can finally resolve.
            await db.ProcessingQueueItems.Where(x => batchIds.Contains(x.PhotoId)).ExecuteDeleteAsync(cancellationToken);
            await db.ProcessingQueues.Where(x => batchIds.Contains(x.PhotoId)).ExecuteDeleteAsync(cancellationToken);
            await db.PhotoVersions.Where(x => batchIds.Contains(x.PhotoId)).ExecuteDeleteAsync(cancellationToken);
            await db.PhotoVersionUrls.Where(x => batchIds.Contains(x.PhotoId)).ExecuteDeleteAsync(cancellationToken);
            await db.PhotoFiles.Where(x => batchIds.Contains(x.PhotoId)).ExecuteDeleteAsync(cancellationToken);
            await db.UserCartItems.Where(x => batchIds.Contains(x.PhotoId)).ExecuteDeleteAsync(cancellationToken);
            await db.Downloads.Where(x => batchIds.Contains(x.PhotoId)).ExecuteDeleteAsync(cancellationToken);

            var deleted = await db.Photos.Where(p => batchIds.Contains(p.Id)).ExecuteDeleteAsync(cancellationToken);
            totalDeleted += deleted;
        }

        _logger.LogWarning(
            "FailedPhotoPurgeService run complete: photosScanned={Scanned} photosDeleted={Deleted}",
            failed.Count, totalDeleted);

        return new FailedPhotoPurgeReport
        {
            PhotosScanned = failed.Count,
            PhotosDeleted = totalDeleted,
            SampleDeletedKeys = failed.Take(20).Select(f => $"{f.AlbumId}/{f.Id} ({f.FileName})").ToList()
        };
    }
}

public class FailedPhotoPurgeReport
{
    public int PhotosScanned { get; set; }
    public int PhotosDeleted { get; set; }
    public List<string> SampleDeletedKeys { get; set; } = new();
}
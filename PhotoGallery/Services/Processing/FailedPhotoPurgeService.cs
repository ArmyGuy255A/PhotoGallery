using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Models;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Hard-deletes Photo rows that the reconciler has marked Failed. Used to
/// clean up DB orphans (Photo rows whose original blob is gone — e.g. after
/// chaos or after a partial upload failure). The accompanying storage
/// blobs, if any, will be cleaned up by the next reaper pass since the
/// Photo row no longer exists to anchor them.
///
/// Destructive — opt-in via the admin Service Health "Enqueue" form. Not
/// scheduled automatically; the admin explicitly chooses when to purge.
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

        // Delete in batches to avoid a single huge transaction. EF Core 9
        // ExecuteDeleteAsync compiles to a single DELETE per call.
        var totalDeleted = 0;
        const int BatchSize = 500;
        for (var offset = 0; offset < failed.Count; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchIds = failed.Skip(offset).Take(BatchSize).Select(f => f.Id).ToList();
            // Cascade is configured on the Photo navigations so PhotoVersions,
            // PhotoVersionUrls, ProcessingQueueItems, and ProcessingQueues are
            // dropped along with the Photo.
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
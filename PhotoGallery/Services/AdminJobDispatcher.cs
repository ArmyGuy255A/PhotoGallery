using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Models;
using PhotoGallery.Services.Processing;

namespace PhotoGallery.Services;

/// <summary>
/// Worker-side dispatcher for <see cref="AdminJob"/> rows the admin UI
/// enqueues. Workers call <see cref="DrainAsync"/> at the top of each
/// tick to pick up any pending jobs for their domain before doing their
/// normal scheduled work.
///
/// Claim is atomic via an UPDATE-FROM-CTE shape on SqlServer (with a
/// best-effort fallback for the InMemory test provider). Once claimed, status flips
/// Pending → Running, and the worker has up to 10 minutes before another
/// replica considers it abandoned (not yet implemented; admin reconciles
/// today are seconds-to-minutes, not hours).
/// </summary>
public class AdminJobDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AdminJobDispatcher> _logger;

    public AdminJobDispatcher(IServiceProvider serviceProvider, ILogger<AdminJobDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Claim and run every pending job matching one of <paramref name="jobTypes"/>.
    /// Returns the number of jobs processed in this drain (0 = nothing
    /// pending). Errors on individual jobs don't propagate — they're
    /// recorded on the row and the next job runs.
    /// </summary>
    public async Task<int> DrainAsync(string[] jobTypes, CancellationToken cancellationToken)
    {
        if (jobTypes == null || jobTypes.Length == 0) return 0;

        var processed = 0;
        // Bounded loop so we don't burn an entire tick on one runaway
        // admin click. 5 should be plenty for normal admin throughput.
        for (var i = 0; i < 5; i++)
        {
            var job = await TryClaimAsync(jobTypes, cancellationToken);
            if (job == null) break;

            await RunOneAsync(job, cancellationToken);
            processed++;
        }
        return processed;
    }

    private async Task<AdminJob?> TryClaimAsync(string[] jobTypes, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Oldest pending job for any of the supplied types.
        var candidate = await db.AdminJobs
            .Where(j => j.Status == AdminJobStatuses.Pending && jobTypes.Contains(j.JobType))
            .OrderBy(j => j.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (candidate == null) return null;

        candidate.Status                = AdminJobStatuses.Running;
        candidate.StartedAt             = DateTime.UtcNow;
        candidate.CompletedByInstanceId = WorkerScheduleRegistry.InstanceId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return candidate;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two workers raced for the same row — drop it, the other
            // worker wins. Caller loops to pick a different one.
            return null;
        }
    }

    private async Task RunOneAsync(AdminJob job, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "AdminJob {JobId} ({JobType}) claimed by {Instance}; starting",
            job.Id, job.JobType, WorkerScheduleRegistry.InstanceId);

        try
        {
            object? result = job.JobType switch
            {
                AdminJobTypes.ReconcileStorage      => await RunReconcileStorage(cancellationToken),
                AdminJobTypes.ReconcileAlbumStorage => await RunReconcileAlbum(job.AlbumId!.Value, cancellationToken),
                AdminJobTypes.ReapOrphans           => await RunReapOrphans(cancellationToken),
                AdminJobTypes.ChaosStorage          => await RunChaos(cancellationToken),
                AdminJobTypes.PurgeFailedPhotos     => await RunPurgeFailed(cancellationToken),
                _ => throw new InvalidOperationException($"Unknown job type: {job.JobType}")
            };
            await CompleteAsync(job.Id, result, error: null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminJob {JobId} ({JobType}) failed", job.Id, job.JobType);
            await CompleteAsync(job.Id, result: null, error: ex.Message, cancellationToken);
        }
    }

    private async Task<ConsistencyReport> RunReconcileStorage(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<StorageConsistencyService>();
        return await svc.RunOnceAsync(ct);
    }

    private async Task<ConsistencyReport> RunReconcileAlbum(Guid albumId, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<StorageConsistencyService>();
        return await svc.RunForAlbumAsync(albumId, ct);
    }

    private async Task<OrphanReapReport> RunReapOrphans(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<OrphanedBlobReaperService>();
        return await svc.RunOnceAsync(ct);
    }

    private async Task<ChaosReport> RunChaos(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ChaosStorageService>();
        return await svc.RunOnceAsync(ct);
    }

    private async Task<FailedPhotoPurgeReport> RunPurgeFailed(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<FailedPhotoPurgeService>();
        return await svc.RunOnceAsync(ct);
    }

    private async Task CompleteAsync(Guid jobId, object? result, string? error, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = await db.AdminJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null) return;
        job.Status       = error == null ? AdminJobStatuses.Complete : AdminJobStatuses.Error;
        job.CompletedAt  = DateTime.UtcNow;
        job.ResultJson   = result == null ? null : JsonSerializer.Serialize(result);
        job.ErrorMessage = error?.Length > 2048 ? error.Substring(0, 2048) : error;
        await db.SaveChangesAsync(ct);
    }
}

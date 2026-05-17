using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Models;

namespace PhotoGallery.Services;

/// <summary>
/// API-side scheduler that periodically enqueues routine maintenance jobs
/// for workers to drain. Replaces the previous timer-driven cycles inside
/// <c>StorageConsistencyWorker</c> and <c>OrphanedBlobReaperWorker</c>:
/// those workers now ONLY pull from the AdminJob queue, and this scheduler
/// is what keeps the queue fed with routine work.
///
/// <para>
/// Runs on the API replica (gated by <c>!WorkersEnabled</c>) so admins always
/// see scheduled rows being created from the API instance. The
/// <see cref="PhotosController.EnqueueAdminJobAsync"/> helper is idempotent
/// on (JobType, AlbumId) — duplicate enqueues are no-ops, so it's safe to
/// scale multiple API replicas (each will try to enqueue but only one row
/// per cycle survives).
/// </para>
///
/// Cadence:
/// <list type="bullet">
///   <item><c>reconcile-storage</c> — every 1 hour.</item>
///   <item><c>reap-orphans</c> — every 6 hours.</item>
/// </list>
/// Both intervals match the legacy timer-driven cycle so behavior is
/// unchanged from the operator's point of view.
/// </summary>
public class AdminJobScheduler : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AdminJobScheduler> _logger;

    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan ReapInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private DateTime _lastReconcileEnqueued = DateTime.MinValue;
    private DateTime _lastReapEnqueued = DateTime.MinValue;

    public AdminJobScheduler(IServiceProvider serviceProvider, ILogger<AdminJobScheduler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AdminJobScheduler started (reconcile every {Reconcile}, reap every {Reap})",
            ReconcileInterval, ReapInterval);

        // Small startup delay so the API finishes booting before we touch
        // the DB. Avoids racing the migration step.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            await EnqueueDueAsync(stoppingToken);
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task EnqueueDueAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (now - _lastReconcileEnqueued >= ReconcileInterval)
            {
                await EnqueueIfNotPresentAsync(AdminJobTypes.ReconcileStorage, cancellationToken);
                _lastReconcileEnqueued = now;
            }
            if (now - _lastReapEnqueued >= ReapInterval)
            {
                await EnqueueIfNotPresentAsync(AdminJobTypes.ReapOrphans, cancellationToken);
                _lastReapEnqueued = now;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AdminJobScheduler tick failed");
        }
    }

    /// <summary>
    /// Mirror of the controller's enqueue helper but standalone (no HTTP
    /// context). Idempotent on (JobType, AlbumId=null): if a row is already
    /// Pending or Running, this is a no-op.
    /// </summary>
    private async Task EnqueueIfNotPresentAsync(string jobType, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var alreadyQueued = await db.AdminJobs.AnyAsync(j =>
            j.JobType == jobType
            && j.AlbumId == null
            && (j.Status == AdminJobStatuses.Pending || j.Status == AdminJobStatuses.Running),
            cancellationToken);
        if (alreadyQueued)
        {
            _logger.LogDebug("AdminJobScheduler skip {JobType}: an entry is already pending or running", jobType);
            return;
        }

        db.AdminJobs.Add(new AdminJob
        {
            Id          = Guid.NewGuid(),
            JobType     = jobType,
            AlbumId     = null,
            Status      = AdminJobStatuses.Pending,
            RequestedAt = DateTime.UtcNow,
            // Distinct sentinel so the audit trail clearly shows scheduler-
            // originated rows vs. admin-clicked ones.
            RequestedBy = "system:scheduler"
        });
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("AdminJobScheduler enqueued routine {JobType}", jobType);
    }
}

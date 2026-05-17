using PhotoGallery.Models;
using PhotoGallery.Services;
namespace PhotoGallery.Services.Processing;

/// <summary>
/// Background service that drains <see cref="AdminJobTypes.ReapOrphans"/>
/// rows from the AdminJob queue. Routine reaps are enqueued by the API's
/// <see cref="AdminJobScheduler"/>; admin-clicked reaps are enqueued by
/// the PhotosController.
///
/// Reference: PhotoGallery v2 plan, Phase 5.
/// </summary>
public sealed class OrphanedBlobReaperWorker : BackgroundService
{
    public const string WorkerName = "OrphanedBlobReaper";

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HeartbeatMinInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrphanedBlobReaperWorker> _logger;
    private readonly WorkerScheduleRegistry _registry;

    public OrphanedBlobReaperWorker(
        IServiceProvider serviceProvider,
        ILogger<OrphanedBlobReaperWorker> logger,
        WorkerScheduleRegistry registry)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _registry = registry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OrphanedBlobReaperWorker started — drains AdminJob queue every {Interval}s",
            TickInterval.TotalSeconds);

        _registry.Register(
            WorkerName,
            displayName: "Orphaned-blob reaper",
            interval: TickInterval,
            triggerHook: null);

        // Stamp a heartbeat IMMEDIATELY on startup so the dashboard sees this
        // worker the moment it boots, instead of waiting up to TickInterval.
        try
        {
            var hb = _serviceProvider.GetRequiredService<WorkerHeartbeatWriter>();
            await hb.StampAsync(WorkerName, "Orphaned-blob reaper", TickInterval, lastRanAt: null, stoppingToken);
        }
        catch { /* heartbeat is best-effort */ }

        try
        {
            DateTime lastHeartbeatAt = DateTime.UtcNow;
            while (!stoppingToken.IsCancellationRequested)
            {
                int drained = 0;
                try
                {
                    var dispatcher = _serviceProvider.GetRequiredService<AdminJobDispatcher>();
                    drained = await dispatcher.DrainAsync(new[] { AdminJobTypes.ReapOrphans }, stoppingToken);
                    if (drained > 0)
                    {
                        var hbWriter = _serviceProvider.GetRequiredService<WorkerHeartbeatWriter>();
                        hbWriter.IncrementProcessed(WorkerName, drained);
                        _logger.LogInformation("OrphanedBlobReaperWorker drained {Count} admin jobs", drained);
                    }
                    _registry.RecordTick(WorkerName);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "AdminJobDispatcher drain failed"); }

                var now = DateTime.UtcNow;
                if (drained > 0 || (now - lastHeartbeatAt) >= HeartbeatMinInterval)
                {
                    try
                    {
                        var hb = _serviceProvider.GetRequiredService<WorkerHeartbeatWriter>();
                        await hb.StampAsync(WorkerName, "Orphaned-blob reaper", TickInterval, now, stoppingToken);
                        lastHeartbeatAt = now;
                    }
                    catch { /* heartbeat is best-effort */ }
                }

                try { await Task.Delay(TickInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("OrphanedBlobReaperWorker stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OrphanedBlobReaperWorker encountered fatal error");
            throw;
        }
    }
}

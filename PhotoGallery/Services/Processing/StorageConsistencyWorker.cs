using PhotoGallery.Models;
using PhotoGallery.Services;
namespace PhotoGallery.Services.Processing;

/// <summary>
/// Background service that drains <see cref="AdminJobTypes.ReconcileStorage"/>
/// and <see cref="AdminJobTypes.ReconcileAlbumStorage"/> rows from the
/// AdminJob queue. Routine maintenance reconciles are enqueued by the API's
/// <see cref="AdminJobScheduler"/>; admin-clicked ones are enqueued by the
/// PhotosController. Either way, this worker pulls them and runs the
/// reconciler.
///
/// Reference: D007 (Storage/Database Consistency Reconciliation).
/// </summary>
public class StorageConsistencyWorker : BackgroundService
{
    public const string WorkerName = "StorageConsistency";

    // Tick fast (every 10s) so admin clicks feel snappy. The dispatcher is
    // a cheap DB query against a tiny table, so polling this often is fine.
    // Heartbeat writes are throttled separately (see HeartbeatMinInterval)
    // so idle polling doesn't write to the DB every tick.
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HeartbeatMinInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StorageConsistencyWorker> _logger;
    private readonly WorkerScheduleRegistry _registry;

    public StorageConsistencyWorker(
        IServiceProvider serviceProvider,
        ILogger<StorageConsistencyWorker> logger,
        WorkerScheduleRegistry registry)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _registry = registry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "StorageConsistencyWorker started — drains AdminJob queue every {Interval}s",
            TickInterval.TotalSeconds);

        // Register with no trigger hook — the AdminJob queue IS the trigger.
        // Anyone wanting an immediate run enqueues a row; no in-process
        // signaling needed.
        _registry.Register(
            WorkerName,
            displayName: "Storage ↔ DB consistency",
            interval: TickInterval,
            triggerHook: null);

        // Stamp a heartbeat IMMEDIATELY so the Service Health dashboard sees
        // this worker the moment it boots, instead of waiting up to TickInterval
        // for the first regular stamp. Without this, freshly-started replicas
        // appear as "no heartbeat yet" until the first tick.
        try
        {
            var hb = _serviceProvider.GetRequiredService<WorkerHeartbeatWriter>();
            await hb.StampAsync(WorkerName, "Storage ↔ DB consistency", TickInterval, lastRanAt: null, stoppingToken);
        }
        catch { /* heartbeat is best-effort */ }

        try
        {
            DateTime lastHeartbeatAt = DateTime.UtcNow;
            while (!stoppingToken.IsCancellationRequested)
            {
                // Resolve the tick interval live each iteration so admin
                // changes to Workers:StorageConsistency:TickIntervalSeconds
                // take effect on the next sleep without restart.
                var tickInterval = await ResolveTickIntervalAsync(stoppingToken);

                int drained = 0;
                try
                {
                    var dispatcher = _serviceProvider.GetRequiredService<AdminJobDispatcher>();
                    drained = await dispatcher.DrainAsync(
                        new[] { AdminJobTypes.ReconcileStorage, AdminJobTypes.ReconcileAlbumStorage, AdminJobTypes.ChaosStorage },
                        stoppingToken);
                    if (drained > 0)
                    {
                        var hbWriter = _serviceProvider.GetRequiredService<WorkerHeartbeatWriter>();
                        hbWriter.IncrementProcessed(WorkerName, drained);
                        _logger.LogInformation("StorageConsistencyWorker drained {Count} admin jobs", drained);
                    }
                    _registry.RecordTick(WorkerName);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "AdminJobDispatcher drain failed"); }

                // Heartbeat throttling: write to the DB only if there was work,
                // or if the alive-window is approaching expiry. Cuts idle write
                // load by ~6x while keeping the dashboard's "alive" detection
                // (90s threshold) reliable.
                var now = DateTime.UtcNow;
                if (drained > 0 || (now - lastHeartbeatAt) >= HeartbeatMinInterval)
                {
                    try
                    {
                        var hb = _serviceProvider.GetRequiredService<WorkerHeartbeatWriter>();
                        await hb.StampAsync(WorkerName, "Storage ↔ DB consistency", tickInterval, now, stoppingToken);
                        lastHeartbeatAt = now;
                    }
                    catch { /* heartbeat is best-effort */ }
                }

                try { await Task.Delay(tickInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("StorageConsistencyWorker stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StorageConsistencyWorker encountered fatal error");
            throw;
        }
    }

    private async Task<TimeSpan> ResolveTickIntervalAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var resolver = scope.ServiceProvider.GetRequiredService<ISettingsResolver>();
            var seconds = Math.Max(1, await resolver.GetIntAsync(
                "Workers:StorageConsistency:TickIntervalSeconds",
                (int)TickInterval.TotalSeconds, ct));
            return TimeSpan.FromSeconds(seconds);
        }
        catch
        {
            return TickInterval;
        }
    }
}

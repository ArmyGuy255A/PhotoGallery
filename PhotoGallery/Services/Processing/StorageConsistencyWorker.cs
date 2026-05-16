using PhotoGallery.Models;
using PhotoGallery.Services;
namespace PhotoGallery.Services.Processing;

/// <summary>
/// Background service that periodically reconciles storage objects against
/// ProcessingQueueItem rows by invoking <see cref="StorageConsistencyService.RunOnceAsync"/>.
///
/// Reference: D007 (Storage/Database Consistency Reconciliation).
///
/// Mirrors the <see cref="PhotoProcessingWorker"/> / <see cref="PhotoVersionUrlRefreshWorker"/>
/// pattern: per-tick scope + live setting resolution.
///
/// Configuration (hot-reloadable via <see cref="ISettingsResolver"/>):
/// <list type="bullet">
///   <item><c>PhotoProcessing:ConsistencyCheckEnabled</c> (default true) — kill switch.</item>
///   <item><c>PhotoProcessing:ConsistencyCheckIntervalHours</c> (default 1) — tick interval.</item>
/// </list>
/// </summary>
public class StorageConsistencyWorker : BackgroundService
{
    public const string WorkerName = "StorageConsistency";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StorageConsistencyWorker> _logger;
    private readonly WorkerScheduleRegistry _registry;
    private readonly bool _defaultEnabled;
    private readonly int _defaultIntervalHours;
    private readonly ManualResetEventSlim _triggerSignal = new(initialState: false);

    public StorageConsistencyWorker(
        IServiceProvider serviceProvider,
        ILogger<StorageConsistencyWorker> logger,
        IConfiguration configuration,
        WorkerScheduleRegistry registry)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _registry = registry;

        _defaultEnabled = configuration.GetValue("PhotoProcessing:ConsistencyCheckEnabled", true);
        _defaultIntervalHours = Math.Max(1,
            configuration.GetValue("PhotoProcessing:ConsistencyCheckIntervalHours", 1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "StorageConsistencyWorker started (default enabled={Enabled}, default interval={IntervalHours}h, hot-reloadable)",
            _defaultEnabled, _defaultIntervalHours);

        _registry.Register(
            WorkerName,
            displayName: "Storage ↔ DB consistency",
            interval: TimeSpan.FromHours(_defaultIntervalHours),
            triggerHook: () => _triggerSignal.Set());

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                bool enabled = _defaultEnabled;
                int intervalHours = _defaultIntervalHours;
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var resolver = scope.ServiceProvider.GetRequiredService<ISettingsResolver>();
                    enabled = await resolver.GetBoolAsync("PhotoProcessing:ConsistencyCheckEnabled", _defaultEnabled, stoppingToken);
                    intervalHours = Math.Max(1,
                        await resolver.GetIntAsync("PhotoProcessing:ConsistencyCheckIntervalHours", _defaultIntervalHours, stoppingToken));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to resolve live settings; using defaults");
                }

                // Drain any admin-queued reconcile jobs FIRST so an admin click
                // gets picked up within seconds rather than waiting for the next
                // scheduled tick (which is hourly).
                try
                {
                    var dispatcher = _serviceProvider.GetRequiredService<AdminJobDispatcher>();
                    var drained = await dispatcher.DrainAsync(
                        new[] { AdminJobTypes.ReconcileStorage, AdminJobTypes.ReconcileAlbumStorage },
                        stoppingToken);
                    if (drained > 0)
                    {
                        _logger.LogInformation("StorageConsistencyWorker drained {Count} admin jobs", drained);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "AdminJobDispatcher drain failed"); }

                if (enabled)
                {
                    try
                    {
                        await RunCycleAsync(stoppingToken);
                        _registry.RecordTick(WorkerName);
                    try
                    {
                        var hb = _serviceProvider.GetRequiredService<WorkerHeartbeatWriter>();
                        await hb.StampAsync(WorkerName, "Storage ↔ DB consistency", TimeSpan.FromHours(intervalHours), DateTime.UtcNow, stoppingToken);
                    }
                    catch { /* heartbeat is best-effort */ }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in storage consistency reconciliation cycle");
                    }
                }
                else
                {
                    _logger.LogDebug("StorageConsistencyWorker tick skipped — disabled via PhotoProcessing:ConsistencyCheckEnabled");
                }

                var triggered = await Task.Run(
                    () => _triggerSignal.Wait(TimeSpan.FromHours(intervalHours), stoppingToken),
                    stoppingToken);
                if (triggered) _triggerSignal.Reset();
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

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<StorageConsistencyService>();
        await service.RunOnceAsync(cancellationToken);
    }
}

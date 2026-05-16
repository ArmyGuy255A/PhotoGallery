namespace PhotoGallery.Services.Processing;

/// <summary>
/// Background service that periodically invokes
/// <see cref="OrphanedBlobReaperService.RunOnceAsync"/> to delete orphaned
/// blobs from storage.
///
/// Reference: PhotoGallery v2 plan, Phase 5.
///
/// Configuration (hot-reloadable via <see cref="ISettingsResolver"/>):
/// <list type="bullet">
///   <item><c>Storage:OrphanReapEnabled</c> (default true) — kill switch. Disabling
///   skips the cycle without stopping the loop, so re-enabling resumes on the
///   next tick.</item>
///   <item><c>Storage:OrphanReapIntervalHours</c> (default 6) — tick interval.
///   Changes take effect on the next sleep cycle.</item>
///   <item><c>Storage:OrphanReapGraceMinutes</c> (default 60) — read by
///   <see cref="OrphanedBlobReaperService"/> per run.</item>
/// </list>
///
/// Registers with <see cref="WorkerScheduleRegistry"/> so the admin Service
/// Health page can see it and trigger a manual run.
/// </summary>
public sealed class OrphanedBlobReaperWorker : BackgroundService
{
    public const string WorkerName = "OrphanedBlobReaper";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrphanedBlobReaperWorker> _logger;
    private readonly WorkerScheduleRegistry _registry;
    private readonly bool _defaultEnabled;
    private readonly int _defaultIntervalHours;
    private readonly ManualResetEventSlim _triggerSignal = new(initialState: false);

    public OrphanedBlobReaperWorker(
        IServiceProvider serviceProvider,
        ILogger<OrphanedBlobReaperWorker> logger,
        IConfiguration configuration,
        WorkerScheduleRegistry registry)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _registry = registry;

        _defaultEnabled = configuration.GetValue("Storage:OrphanReapEnabled", true);
        _defaultIntervalHours = Math.Max(1,
            configuration.GetValue("Storage:OrphanReapIntervalHours", 6));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OrphanedBlobReaperWorker started (default enabled={Enabled}, default interval={IntervalHours}h, hot-reloadable)",
            _defaultEnabled, _defaultIntervalHours);

        _registry.Register(
            WorkerName,
            displayName: "Orphaned-blob reaper",
            interval: TimeSpan.FromHours(_defaultIntervalHours),
            triggerHook: () => _triggerSignal.Set());

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Read enabled + interval live so admin changes take effect
                // on the next tick.
                bool enabled = _defaultEnabled;
                int intervalHours = _defaultIntervalHours;
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var resolver = scope.ServiceProvider.GetRequiredService<ISettingsResolver>();
                    enabled = await resolver.GetBoolAsync("Storage:OrphanReapEnabled", _defaultEnabled, stoppingToken);
                    intervalHours = Math.Max(1,
                        await resolver.GetIntAsync("Storage:OrphanReapIntervalHours", _defaultIntervalHours, stoppingToken));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to resolve live settings; using defaults");
                }

                if (enabled)
                {
                    try
                    {
                        await RunCycleAsync(stoppingToken);
                        _registry.RecordTick(WorkerName);
                    try
                    {
                        var hb = _serviceProvider.GetRequiredService<WorkerHeartbeatWriter>();
                        await hb.StampAsync(WorkerName, "Orphaned-blob reaper", TimeSpan.FromHours(intervalHours), DateTime.UtcNow, stoppingToken);
                    }
                    catch { /* heartbeat is best-effort */ }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in orphaned blob reaper cycle");
                    }
                }
                else
                {
                    _logger.LogDebug("OrphanedBlobReaperWorker tick skipped — disabled via Storage:OrphanReapEnabled");
                }

                var triggered = await Task.Run(
                    () => _triggerSignal.Wait(TimeSpan.FromHours(intervalHours), stoppingToken),
                    stoppingToken);
                if (triggered) _triggerSignal.Reset();
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

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<OrphanedBlobReaperService>();
        await service.RunOnceAsync(cancellationToken);
    }
}

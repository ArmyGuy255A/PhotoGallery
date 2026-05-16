using PhotoGallery.Interfaces;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Background service that periodically processes queued photos. Drives
/// the image-resize + watermark pipeline through <see cref="IImageProcessor.ProcessQueueAsync"/>.
///
/// Ref: D003 (Image Processing with Compression Profiles).
/// </summary>
public class PhotoProcessingWorker : BackgroundService
{
    public const string WorkerName = "PhotoProcessing";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PhotoProcessingWorker> _logger;
    private readonly WorkerScheduleRegistry _registry;
    private readonly int _defaultIntervalSeconds;
    /// <summary>
    /// Signal flipped by <see cref="WorkerScheduleRegistry.Trigger"/> to
    /// short-circuit the periodic timer. Set + immediately reset so the
    /// worker fires one extra tick now and then resumes its normal cadence.
    /// </summary>
    private readonly ManualResetEventSlim _triggerSignal = new(initialState: false);

    public PhotoProcessingWorker(
        IServiceProvider serviceProvider,
        ILogger<PhotoProcessingWorker> logger,
        IConfiguration configuration,
        WorkerScheduleRegistry registry)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _registry = registry;

        // Construction-time default. The actual per-tick interval is read live
        // from ISettingsResolver inside ExecuteAsync so admins can hot-reload
        // PhotoProcessing:IntervalSeconds without a restart.
        _defaultIntervalSeconds = configuration.GetValue<int>("PhotoProcessing:IntervalSeconds", 5);
        if (_defaultIntervalSeconds < 1)
            _defaultIntervalSeconds = 5;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PhotoProcessingWorker started with default {IntervalSeconds}s interval (live-overridable)", _defaultIntervalSeconds);
        _registry.Register(
            WorkerName,
            displayName: "Image processing queue",
            interval: TimeSpan.FromSeconds(_defaultIntervalSeconds),
            triggerHook: () => _triggerSignal.Set());

        try
        {
            int currentInterval = _defaultIntervalSeconds;
            while (!stoppingToken.IsCancellationRequested)
            {
                // Read interval live FIRST so heartbeat reports the current cadence.
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var resolver = scope.ServiceProvider.GetRequiredService<ISettingsResolver>();
                    currentInterval = Math.Max(1,
                        await resolver.GetIntAsync("PhotoProcessing:IntervalSeconds", _defaultIntervalSeconds, stoppingToken));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to resolve live IntervalSeconds; using default {Default}", _defaultIntervalSeconds);
                }

                string? lastError = null;
                try
                {
                    await ProcessPhotosAsync(stoppingToken);
                    _registry.RecordTick(WorkerName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in photo processing cycle");
                    lastError = ex.Message;
                }

                try
                {
                    var hb = _serviceProvider.GetRequiredService<WorkerHeartbeatWriter>();
                    await hb.StampAsync(
                        WorkerName,
                        "Image processing queue",
                        TimeSpan.FromSeconds(currentInterval),
                        lastError == null ? DateTime.UtcNow : (DateTime?)null,
                        stoppingToken,
                        itemsInFlight: 0,
                        lastError: lastError);
                }
                catch { /* heartbeat is best-effort */ }

                // Wait either for the live interval OR a manual trigger,
                // whichever comes first. The trigger signal short-circuits
                // the wait so admins can kick off an immediate run.
                var triggered = await Task.Run(() => _triggerSignal.Wait(TimeSpan.FromSeconds(currentInterval), stoppingToken), stoppingToken);
                if (triggered) _triggerSignal.Reset();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("PhotoProcessingWorker stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhotoProcessingWorker encountered fatal error");
            throw;
        }
    }

    private async Task ProcessPhotosAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var imageProcessor = scope.ServiceProvider.GetRequiredService<IImageProcessor>();

            _logger.LogDebug("Starting photo processing cycle");
            await imageProcessor.ProcessQueueAsync(cancellationToken);
            _logger.LogDebug("Completed photo processing cycle");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in ProcessPhotosAsync");
            throw;
        }
    }
}

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
    private readonly int _intervalSeconds;
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

        // Read interval from config, default to 5 seconds. Admin overrides
        // applied via the RuntimeSettings table take effect on restart
        // (the catalogue entry flags this).
        _intervalSeconds = configuration.GetValue<int>("PhotoProcessing:IntervalSeconds", 5);
        if (_intervalSeconds < 1)
            _intervalSeconds = 5;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PhotoProcessingWorker started with {IntervalSeconds}s interval", _intervalSeconds);
        _registry.Register(
            WorkerName,
            displayName: "Image processing queue",
            interval: TimeSpan.FromSeconds(_intervalSeconds),
            triggerHook: () => _triggerSignal.Set());

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPhotosAsync(stoppingToken);
                    _registry.RecordTick(WorkerName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in photo processing cycle");
                }

                // Wait either for the normal interval OR a manual trigger,
                // whichever comes first. The trigger signal short-circuits
                // the wait so admins can kick off an immediate run.
                var triggered = await Task.Run(() => _triggerSignal.Wait(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken), stoppingToken);
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

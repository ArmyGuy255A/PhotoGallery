using PhotoGallery.Interfaces;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Background service that periodically processes queued photos.
/// Runs on a configurable interval to create compressed versions of uploaded photos.
/// Ref: D003 (Image Processing with Compression Profiles)
/// </summary>
public class PhotoProcessingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PhotoProcessingWorker> _logger;
    private readonly int _intervalSeconds;

    public PhotoProcessingWorker(
        IServiceProvider serviceProvider,
        ILogger<PhotoProcessingWorker> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        // Read interval from config, default to 5 seconds
        _intervalSeconds = configuration.GetValue<int>("PhotoProcessing:IntervalSeconds", 5);
        
        if (_intervalSeconds < 1)
            _intervalSeconds = 5;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PhotoProcessingWorker started with {IntervalSeconds}s interval", _intervalSeconds);

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ProcessPhotosAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in photo processing cycle");
                }
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

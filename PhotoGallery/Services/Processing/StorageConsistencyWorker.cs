namespace PhotoGallery.Services.Processing;

/// <summary>
/// Background service that periodically reconciles storage objects against
/// ProcessingQueueItem rows by invoking <see cref="StorageConsistencyService.RunOnceAsync"/>.
///
/// Reference: D007 (Storage/Database Consistency Reconciliation).
///
/// Mirrors the <see cref="PhotoProcessingWorker"/> / <see cref="PhotoVersionUrlRefreshWorker"/>
/// pattern: PeriodicTimer + per-tick scope. The worker holds no per-cycle state; the
/// scoped <c>StorageConsistencyService</c> owns the reconciliation logic and its own
/// SemaphoreSlim so an overlapping admin-triggered run cannot conflict with a tick.
///
/// Configuration:
/// <list type="bullet">
///   <item><c>PhotoProcessing:ConsistencyCheckEnabled</c> (default true) — kill switch.</item>
///   <item><c>PhotoProcessing:ConsistencyCheckIntervalHours</c> (default 1) — tick interval.</item>
/// </list>
/// </summary>
public class StorageConsistencyWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StorageConsistencyWorker> _logger;
    private readonly bool _enabled;
    private readonly int _intervalHours;

    public StorageConsistencyWorker(
        IServiceProvider serviceProvider,
        ILogger<StorageConsistencyWorker> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        _enabled = configuration.GetValue("PhotoProcessing:ConsistencyCheckEnabled", true);
        _intervalHours = configuration.GetValue("PhotoProcessing:ConsistencyCheckIntervalHours", 1);
        if (_intervalHours < 1)
        {
            _intervalHours = 1;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("StorageConsistencyWorker disabled by PhotoProcessing:ConsistencyCheckEnabled=false");
            return;
        }

        _logger.LogInformation("StorageConsistencyWorker started with {IntervalHours}h interval", _intervalHours);

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(_intervalHours));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunCycleAsync(stoppingToken);
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

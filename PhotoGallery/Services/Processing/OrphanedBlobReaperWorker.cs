namespace PhotoGallery.Services.Processing;

/// <summary>
/// Background service that periodically invokes
/// <see cref="OrphanedBlobReaperService.RunOnceAsync"/> to delete orphaned
/// blobs from storage.
///
/// Reference: PhotoGallery v2 plan, Phase 5.
///
/// Configuration:
/// <list type="bullet">
///   <item><c>Storage:OrphanReapEnabled</c> (default true) — kill switch.</item>
///   <item><c>Storage:OrphanReapIntervalHours</c> (default 6) — tick interval.</item>
///   <item><c>Storage:OrphanReapGraceMinutes</c> (default 60) — read by the service itself.</item>
/// </list>
///
/// Lifetime: singleton (per <c>BackgroundService</c> convention). Resolves a
/// fresh <c>OrphanedBlobReaperService</c> scope per tick so EF Core scoped
/// repositories work normally.
/// </summary>
public sealed class OrphanedBlobReaperWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrphanedBlobReaperWorker> _logger;
    private readonly bool _enabled;
    private readonly int _intervalHours;

    public OrphanedBlobReaperWorker(
        IServiceProvider serviceProvider,
        ILogger<OrphanedBlobReaperWorker> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        _enabled = configuration.GetValue("Storage:OrphanReapEnabled", true);
        _intervalHours = configuration.GetValue("Storage:OrphanReapIntervalHours", 6);
        if (_intervalHours < 1)
        {
            _intervalHours = 1;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("OrphanedBlobReaperWorker disabled by Storage:OrphanReapEnabled=false");
            return;
        }

        _logger.LogInformation("OrphanedBlobReaperWorker started with {IntervalHours}h interval", _intervalHours);

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
                    _logger.LogError(ex, "Error in orphaned blob reaper cycle");
                }
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

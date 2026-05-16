using PhotoGallery.Interfaces;
using PhotoGallery.Services;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Background service that periodically refreshes expiring pre-signed URLs for photo downloads.
///
/// Responsibilities:
/// - Runs on a configurable interval (default daily) to check for URLs approaching expiration
/// - Regenerates URLs when they're within the refresh window (default: 5 days before expiration)
/// - Ensures Thumbnail and Medium URLs never expire in the database
/// - Logs refresh operations and any errors
///
/// Reference: D004 (Pre-Signed URL Caching Architecture)
///
/// Configuration (hot-reloadable via <see cref="ISettingsResolver"/>):
/// <list type="bullet">
///   <item><c>BlobStorage:RefreshWorkerIntervalHours</c> (default 24) — tick interval.</item>
///   <item><c>BlobStorage:PreSignedUrlRefreshWindowDays</c> (default 5) — refresh window.</item>
/// </list>
/// </summary>
public class PhotoVersionUrlRefreshWorker : BackgroundService
{
    public const string WorkerName = "PhotoVersionUrlRefresh";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PhotoVersionUrlRefreshWorker> _logger;
    private readonly WorkerScheduleRegistry _registry;
    private readonly int _defaultIntervalHours;
    private readonly int _defaultRefreshWindowDays;
    private readonly ManualResetEventSlim _triggerSignal = new(initialState: false);

    public PhotoVersionUrlRefreshWorker(
        IServiceProvider serviceProvider,
        ILogger<PhotoVersionUrlRefreshWorker> logger,
        IConfiguration configuration,
        WorkerScheduleRegistry registry)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _registry = registry;

        _defaultIntervalHours = Math.Max(1,
            configuration.GetValue("BlobStorage:RefreshWorkerIntervalHours", 24));
        _defaultRefreshWindowDays = configuration.GetValue("BlobStorage:PreSignedUrlRefreshWindowDays", 5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PhotoVersionUrlRefreshWorker started (default interval={IntervalHours}h, refresh-window={WindowDays}d, hot-reloadable)",
            _defaultIntervalHours, _defaultRefreshWindowDays);

        _registry.Register(
            WorkerName,
            displayName: "Pre-signed URL refresh",
            interval: TimeSpan.FromHours(_defaultIntervalHours),
            triggerHook: () => _triggerSignal.Set());

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                int intervalHours = _defaultIntervalHours;
                int refreshWindowDays = _defaultRefreshWindowDays;
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var resolver = scope.ServiceProvider.GetRequiredService<ISettingsResolver>();
                    intervalHours = Math.Max(1,
                        await resolver.GetIntAsync("BlobStorage:RefreshWorkerIntervalHours", _defaultIntervalHours, stoppingToken));
                    refreshWindowDays = await resolver.GetIntAsync("BlobStorage:PreSignedUrlRefreshWindowDays", _defaultRefreshWindowDays, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to resolve live settings; using defaults");
                }

                try
                {
                    await RefreshExpiringUrlsAsync(refreshWindowDays, intervalHours, stoppingToken);
                    _registry.RecordTick(WorkerName);
                    try
                    {
                        var hb = _serviceProvider.GetRequiredService<WorkerHeartbeatWriter>();
                        await hb.StampAsync(WorkerName, "Pre-signed URL refresh", TimeSpan.FromHours(intervalHours), DateTime.UtcNow, stoppingToken);
                    }
                    catch { /* heartbeat is best-effort */ }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in photo URL refresh cycle");
                }

                var triggered = await Task.Run(
                    () => _triggerSignal.Wait(TimeSpan.FromHours(intervalHours), stoppingToken),
                    stoppingToken);
                if (triggered) _triggerSignal.Reset();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("PhotoVersionUrlRefreshWorker stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhotoVersionUrlRefreshWorker encountered fatal error");
            throw;
        }
    }

    private async Task RefreshExpiringUrlsAsync(int refreshWindowDays, int intervalHours, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var urlRepository = scope.ServiceProvider.GetRequiredService<IPhotoVersionUrlRepository>();
            var urlService = scope.ServiceProvider.GetRequiredService<PhotoVersionUrlService>();

            var now = DateTime.UtcNow;
            var refreshCutoffDate = now.AddDays(refreshWindowDays);

            _logger.LogDebug("Starting URL refresh cycle. Looking for URLs expiring before {CutoffDate}", refreshCutoffDate);

            var expiringUrls = await urlRepository.GetExpiringAsync(refreshCutoffDate);

            if (expiringUrls.Count == 0)
            {
                _logger.LogDebug("No expiring URLs found. Next check in {IntervalHours}h", intervalHours);
                return;
            }

            _logger.LogInformation("Found {Count} URLs expiring within {WindowDays} days. Refreshing...", expiringUrls.Count, refreshWindowDays);

            var successCount = 0;
            var failureCount = 0;

            var urlsByPhoto = expiringUrls.GroupBy(u => u.PhotoId);

            foreach (var photoGroup in urlsByPhoto)
            {
                try
                {
                    var photoId = photoGroup.Key;
                    var newUrls = await urlService.GeneratePhotoVersionUrlsAsync(photoId);

                    var successfulQualities = newUrls.Count(kvp => kvp.Value != null);
                    _logger.LogInformation("Refreshed {Count} URLs for photo {PhotoId}", successfulQualities, photoId);
                    successCount += successfulQualities;
                    failureCount += (4 - successfulQualities);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error refreshing URLs for photo {PhotoId}", photoGroup.Key);
                    failureCount += photoGroup.Count();
                }
            }

            _logger.LogInformation("URL refresh cycle completed. Successes: {SuccessCount}, Failures: {FailureCount}", successCount, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in RefreshExpiringUrlsAsync");
            throw;
        }
    }
}

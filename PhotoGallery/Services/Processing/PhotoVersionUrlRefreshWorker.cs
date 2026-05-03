using PhotoGallery.Interfaces;
using PhotoGallery.Services;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Background service that periodically refreshes expiring pre-signed URLs for photo downloads.
/// 
/// Responsibilities:
/// - Runs daily (configurable) to check for URLs approaching expiration
/// - Regenerates URLs when they're within the refresh window (default: 5 days before expiration)
/// - Ensures Thumbnail and Medium URLs never expire in the database
/// - Logs refresh operations and any errors
/// 
/// Reference: D004 (Pre-Signed URL Caching Architecture)
/// </summary>
public class PhotoVersionUrlRefreshWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PhotoVersionUrlRefreshWorker> _logger;
    private readonly int _refreshIntervalHours;
    private readonly int _refreshWindowDays;

    public PhotoVersionUrlRefreshWorker(
        IServiceProvider serviceProvider,
        ILogger<PhotoVersionUrlRefreshWorker> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Read interval from config, default to 24 hours (daily)
        _refreshIntervalHours = configuration.GetValue<int>("BlobStorage:RefreshWorkerIntervalHours", 24);
        if (_refreshIntervalHours < 1)
            _refreshIntervalHours = 24;

        // Read refresh window from config, default to 5 days
        _refreshWindowDays = configuration.GetValue<int>("BlobStorage:PreSignedUrlRefreshWindowDays", 5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PhotoVersionUrlRefreshWorker started with {IntervalHours}h interval", _refreshIntervalHours);

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(_refreshIntervalHours));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RefreshExpiringUrlsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in photo URL refresh cycle");
                }
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

    private async Task RefreshExpiringUrlsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var urlRepository = scope.ServiceProvider.GetRequiredService<IPhotoVersionUrlRepository>();
            var urlService = scope.ServiceProvider.GetRequiredService<PhotoVersionUrlService>();

            var now = DateTime.UtcNow;
            var refreshCutoffDate = now.AddDays(_refreshWindowDays);

            _logger.LogDebug("Starting URL refresh cycle. Looking for URLs expiring before {CutoffDate}", refreshCutoffDate);

            // Get all URLs that are expiring within the refresh window
            var expiringUrls = await urlRepository.GetExpiringAsync(refreshCutoffDate);

            if (expiringUrls.Count == 0)
            {
                _logger.LogDebug("No expiring URLs found. Next check in {IntervalHours}h", _refreshIntervalHours);
                return;
            }

            _logger.LogInformation("Found {Count} URLs expiring within {WindowDays} days. Refreshing...", expiringUrls.Count, _refreshWindowDays);

            var successCount = 0;
            var failureCount = 0;

            // Group by photo to refresh efficiently
            var urlsByPhoto = expiringUrls.GroupBy(u => u.PhotoId);

            foreach (var photoGroup in urlsByPhoto)
            {
                try
                {
                    var photoId = photoGroup.Key;

                    // Generate new URLs for all qualities (will update cached ones)
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

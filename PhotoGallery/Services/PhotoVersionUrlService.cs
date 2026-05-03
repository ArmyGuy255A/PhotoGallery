using Microsoft.Extensions.Configuration;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Storage;

namespace PhotoGallery.Services;

/// <summary>
/// Manages pre-signed URLs for photo versions to enable direct downloads from blob storage.
/// 
/// Responsibilities:
/// - Generate pre-signed MinIO URLs for all 4 photo qualities
/// - Cache Thumbnail and Medium URLs in database with TTL
/// - Return cached URLs if valid, regenerate if expired
/// - Support on-demand generation for Low/High qualities (shopping cart feature)
/// 
/// Reference: D004 (Pre-Signed URL Caching Architecture)
/// </summary>
public class PhotoVersionUrlService
{
    private readonly IStorageProvider _storageProvider;
    private readonly IPhotoVersionUrlRepository _urlRepository;
    private readonly IPhotoRepository _photoRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PhotoVersionUrlService> _logger;

    private readonly int _ttlDays;
    private readonly int _refreshWindowDays;
    private readonly List<string> _cachedQualities;

    public PhotoVersionUrlService(
        IStorageProvider storageProvider,
        IPhotoVersionUrlRepository urlRepository,
        IPhotoRepository photoRepository,
        IConfiguration configuration,
        ILogger<PhotoVersionUrlService> logger)
    {
        _storageProvider = storageProvider;
        _urlRepository = urlRepository;
        _photoRepository = photoRepository;
        _configuration = configuration;
        _logger = logger;

        // Load configuration
        _ttlDays = _configuration.GetValue("BlobStorage:PreSignedUrlTTLDays", 7);
        _refreshWindowDays = _configuration.GetValue("BlobStorage:PreSignedUrlRefreshWindowDays", 5);
        var cachedQualitiesJson = _configuration.GetSection("BlobStorage:CachedQualities").Get<List<string>>() ?? new();
        _cachedQualities = cachedQualitiesJson;
    }

    /// <summary>
    /// Get a pre-signed URL for a photo quality. Returns cached URL if valid, otherwise generates new one.
    /// Only Thumbnail and Medium are cached in database. Low/High are generated on-demand.
    /// </summary>
    public async Task<string?> GetPhotoVersionUrlAsync(Guid photoId, QualityType quality)
    {
        try
        {
            // Check if this quality should be cached
            var shouldCache = ShouldCacheQuality(quality);

            if (shouldCache)
            {
                // Try to get cached URL
                var cachedUrl = await _urlRepository.GetByPhotoAndQualityAsync(photoId, quality);
                
                if (cachedUrl != null && cachedUrl.IsActive && cachedUrl.ExpiresAt > DateTime.UtcNow)
                {
                    _logger.LogInformation("Returning cached URL for photo {PhotoId} quality {Quality}", photoId, quality);
                    return cachedUrl.PresignedUrl;
                }
            }

            // Generate new URL
            return await GeneratePhotoVersionUrlAsync(photoId, quality, shouldCache);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting photo version URL for photo {PhotoId} quality {Quality}", photoId, quality);
            return null;
        }
    }

    /// <summary>
    /// Generate pre-signed URLs for all 4 photo qualities and cache Thumbnail/Medium in database.
    /// Called during photo processing after successful resize/encode.
    /// </summary>
    public async Task<Dictionary<QualityType, string?>> GeneratePhotoVersionUrlsAsync(Guid photoId)
    {
        var result = new Dictionary<QualityType, string?>();
        
        try
        {
            var photo = await _photoRepository.GetByIdAsync(photoId);
            if (photo == null)
            {
                _logger.LogWarning("Photo {PhotoId} not found, cannot generate URLs", photoId);
                return result;
            }

            var now = DateTime.UtcNow;
            var expiresAt = now.AddDays(_ttlDays);

            // Generate URLs for all 4 qualities
            foreach (QualityType quality in Enum.GetValues(typeof(QualityType)))
            {
                try
                {
                    var url = await GeneratePhotoVersionUrlAsync(photoId, quality, shouldCache: ShouldCacheQuality(quality));
                    result[quality] = url;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate URL for photo {PhotoId} quality {Quality}", photoId, quality);
                    result[quality] = null;
                }
            }

            _logger.LogInformation("Generated pre-signed URLs for photo {PhotoId}: {Qualities}", photoId, string.Join(", ", result.Keys));
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating photo version URLs for photo {PhotoId}", photoId);
            return result;
        }
    }

    private async Task<string?> GeneratePhotoVersionUrlAsync(Guid photoId, QualityType quality, bool shouldCache)
    {
        try
        {
            var photo = await _photoRepository.GetByIdAsync(photoId);
            if (photo == null)
            {
                _logger.LogWarning("Photo {PhotoId} not found when generating URL for quality {Quality}", photoId, quality);
                return null;
            }

            // Construct storage key: photogallery/{albumId}/{photoId}/{quality}.jpg
            var qualityName = quality.ToString().ToLower();
            var storageKey = $"photogallery/{photo.AlbumId}/{photoId}/{qualityName}.jpg";

            // Verify file exists
            var exists = await _storageProvider.ExistsAsync(storageKey);
            if (!exists)
            {
                _logger.LogWarning("Photo version file not found in storage: {StorageKey}", storageKey);
                return null;
            }

            // Generate pre-signed URL (TTL in minutes)
            var expirationMinutes = _ttlDays * 24 * 60;  // Convert days to minutes
            var presignedUrl = await _storageProvider.GetUrlAsync(storageKey, expirationMinutes);

            if (string.IsNullOrEmpty(presignedUrl))
            {
                _logger.LogError("Failed to generate pre-signed URL for storage key: {StorageKey}", storageKey);
                return null;
            }

            // Cache if applicable
            if (shouldCache)
            {
                await CachePhotoVersionUrlAsync(photoId, quality, presignedUrl);
            }

            return presignedUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating URL for photo {PhotoId} quality {Quality}", photoId, quality);
            return null;
        }
    }

    private async Task CachePhotoVersionUrlAsync(Guid photoId, QualityType quality, string presignedUrl)
    {
        try
        {
            var now = DateTime.UtcNow;
            var expiresAt = now.AddDays(_ttlDays);

            // Check if URL already exists and should be updated
            var existing = await _urlRepository.GetByPhotoAndQualityAsync(photoId, quality);

            if (existing != null)
            {
                // Update existing
                existing.PresignedUrl = presignedUrl;
                existing.ExpiresAt = expiresAt;
                existing.GeneratedAt = now;
                existing.IsActive = true;
                await _urlRepository.UpdateAsync(existing);
            }
            else
            {
                // Create new
                var urlEntry = new PhotoVersionUrl
                {
                    Id = Guid.NewGuid(),
                    PhotoId = photoId,
                    Quality = quality,
                    PresignedUrl = presignedUrl,
                    ExpiresAt = expiresAt,
                    GeneratedAt = now,
                    IsActive = true
                };
                await _urlRepository.AddAsync(urlEntry);
            }

            await _urlRepository.SaveChangesAsync();
            _logger.LogInformation("Cached pre-signed URL for photo {PhotoId} quality {Quality}, expires at {ExpiresAt}", photoId, quality, expiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching URL for photo {PhotoId} quality {Quality}", photoId, quality);
            // Don't rethrow - URL generation is complete, caching is optimization
        }
    }

    private bool ShouldCacheQuality(QualityType quality)
    {
        var qualityName = quality.ToString();
        return _cachedQualities.Contains(qualityName, StringComparer.OrdinalIgnoreCase);
    }
}

using PhotoGallery.Data.Repositories;
using PhotoGallery.Models;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// Ensures all processed photos have all 4 quality versions in storage.
/// Reference: D003 (Image Processing with Compression Profiles)
/// </summary>
public class PhotoConsistencyChecker
{
    private readonly IProcessingQueueRepository _queueRepository;
    private readonly IProcessingQueueItemRepository _itemRepository;
    private readonly ILogger<PhotoConsistencyChecker> _logger;

    public PhotoConsistencyChecker(
        IProcessingQueueRepository queueRepository,
        IProcessingQueueItemRepository itemRepository,
        ILogger<PhotoConsistencyChecker> logger)
    {
        _queueRepository = queueRepository;
        _itemRepository = itemRepository;
        _logger = logger;
    }

    /// <summary>
    /// Verify all 4 base quality versions exist and are Complete for the given photo.
    /// The optional Watermark queue item (Phase 4 §2) is intentionally excluded from
    /// this check: watermark rendering is best-effort and not part of "photo viewable".
    /// </summary>
    public async Task<bool> VerifyPhotoCompleteAsync(Guid photoId)
    {
        try
        {
            var items = await _itemRepository.GetByPhotoIdAsync(photoId);
            // Exclude Watermark items — those are an independent retry track.
            var items_list = items.Where(i => i.Quality != QualityType.Watermark).ToList();

            // Must have exactly 4 items (Thumbnail/Low/Medium/High)
            if (items_list.Count != 4)
            {
                _logger.LogWarning("Photo {PhotoId} has {Count} base-quality items, expected 4", photoId, items_list.Count);
                return false;
            }

            // All must be Complete status
            if (!items_list.All(i => i.Status == ProcessingStatus.Complete))
            {
                var incomplete = items_list.Where(i => i.Status != ProcessingStatus.Complete).Select(i => i.Quality);
                _logger.LogWarning("Photo {PhotoId} incomplete qualities: {Qualities}", photoId, string.Join(",", incomplete));
                return false;
            }

            // Must have all 4 base quality types
            var qualities = items_list.Select(i => i.Quality).ToHashSet();
            if (qualities.Count != 4 || !qualities.Contains(QualityType.Thumbnail) || !qualities.Contains(QualityType.Low) ||
                !qualities.Contains(QualityType.Medium) || !qualities.Contains(QualityType.High))
            {
                _logger.LogWarning("Photo {PhotoId} missing quality types", photoId);
                return false;
            }

            _logger.LogInformation("Photo {PhotoId} verified complete with all 4 quality versions", photoId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying photo {PhotoId}", photoId);
            return false;
        }
    }

    /// <summary>
    /// Mark queue as complete if every base-quality item is Complete. The Watermark
    /// item, if present, is excluded — the queue's overall lifecycle tracks the
    /// resize variants only; the watermark item is a side track.
    /// </summary>
    public async Task MarkQueueCompleteIfReadyAsync(Guid queueId)
    {
        try
        {
            var queue = await _queueRepository.GetByIdAsync(queueId);
            if (queue == null || queue.Status == ProcessingStatus.Complete)
                return;

            var items = await _itemRepository.GetByQueueIdAsync(queueId);
            var items_list = items.Where(i => i.Quality != QualityType.Watermark).ToList();

            if (items_list.Count > 0 && items_list.All(i => i.Status == ProcessingStatus.Complete))
            {
                queue.MarkComplete();
                await _queueRepository.UpdateAsync(queue);
                await _queueRepository.SaveChangesAsync();
                _logger.LogInformation("Queue {QueueId} marked complete", queueId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking queue {QueueId} completion", queueId);
        }
    }
}

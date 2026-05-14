using PhotoGallery.Models;
using PhotoGallery.Interfaces;

namespace PhotoGallery.Data.Repositories;

/// <summary>
/// Repository interface for ProcessingQueueItem operations.
/// Reference: D003 (Image Processing with Compression Profiles)
/// </summary>
public interface IProcessingQueueItemRepository : IRepository<ProcessingQueueItem>
{
    /// <summary>
    /// Get all items eligible for the next processing pass: <c>Status = Pending</c>
    /// OR <c>Status = Error AND (NextRetryTime IS NULL OR NextRetryTime &lt;= now)</c>.
    /// Items with an active lease (<c>LeaseExpiresAt &gt; now</c>) are excluded.
    /// Reference: Phase 4 scope §1 (dead-letter removal — every item eventually retries).
    /// </summary>
    Task<IEnumerable<ProcessingQueueItem>> GetPendingItemsAsync();

    /// <summary>Get all items ready for retry (NextRetryTime has passed, status is Error)</summary>
    Task<IEnumerable<ProcessingQueueItem>> GetReadyForRetryAsync();

    /// <summary>Get all items for a specific processing queue</summary>
    Task<IEnumerable<ProcessingQueueItem>> GetByQueueIdAsync(Guid queueId);

    /// <summary>Get all items for a specific photo</summary>
    Task<IEnumerable<ProcessingQueueItem>> GetByPhotoIdAsync(Guid photoId);

    /// <summary>Mark an item as completed</summary>
    Task MarkCompleteAsync(Guid itemId);

    /// <summary>Mark an item as failed with an error message</summary>
    Task MarkFailedAsync(Guid itemId, string errorMessage);

    /// <summary>
    /// Atomically claim a batch of work via the DB-level lease (Phase 4 §4).
    /// Selects up to <paramref name="batchSize"/> rows where <c>Status = Pending</c>
    /// (or <c>Status = Error</c> with retry due) AND the lease is free, sets
    /// <c>LeaseExpiresAt = now + leaseDuration</c>, and returns the leased rows.
    /// Expired leases naturally fall back into the queue so a worker crash never
    /// drops an item permanently.
    /// </summary>
    Task<IReadOnlyList<ProcessingQueueItem>> LeaseNextBatchAsync(int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear the lease on an item so other workers can pick it up. Called after a
    /// failed attempt so retry pickup is not blocked by the original worker's lease.
    /// </summary>
    Task ReleaseLeaseAsync(Guid itemId, CancellationToken cancellationToken = default);
}

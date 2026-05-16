using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Repositories;

/// <summary>
/// Repository implementation for ProcessingQueueItem operations.
/// Reference: D003 (Image Processing with Compression Profiles)
/// </summary>
public class ProcessingQueueItemRepository : Repository<ProcessingQueueItem>, IProcessingQueueItemRepository
{
    public ProcessingQueueItemRepository(ApplicationDbContext context) : base(context)
    {
    }

    /// <summary>
    /// Get all items eligible for the next pass: Pending, or Error with a retry time
    /// that has come due (or never set). Items with an active lease are excluded so
    /// a worker mid-flight doesn't get its row scooped by a peer. Phase 4 §1.
    /// </summary>
    public async Task<IEnumerable<ProcessingQueueItem>> GetPendingItemsAsync()
    {
        var now = DateTime.UtcNow;
        return await _dbSet
            .Where(item =>
                (item.Status == ProcessingStatus.Pending ||
                 (item.Status == ProcessingStatus.Error &&
                  (item.NextRetryTime == null || item.NextRetryTime <= now)))
                && (item.LeaseExpiresAt == null || item.LeaseExpiresAt < now))
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Get all items in Error status whose retry time has elapsed. Under Phase 4 there
    /// is no max-retry cap; this method now returns every Error item whose backoff has
    /// expired (so it folds into <see cref="GetPendingItemsAsync"/> semantically, but
    /// callers that expect only the retry slice still work).
    /// </summary>
    public async Task<IEnumerable<ProcessingQueueItem>> GetReadyForRetryAsync()
    {
        var now = DateTime.UtcNow;
        return await _dbSet
            .Where(item => item.Status == ProcessingStatus.Error &&
                          item.NextRetryTime.HasValue &&
                          item.NextRetryTime.Value <= now &&
                          (item.LeaseExpiresAt == null || item.LeaseExpiresAt < now))
            .OrderBy(item => item.NextRetryTime)
            .ToListAsync();
    }

    /// <summary>Get all items for a specific processing queue</summary>
    public async Task<IEnumerable<ProcessingQueueItem>> GetByQueueIdAsync(Guid queueId)
    {
        return await _dbSet
            .Where(item => item.ProcessingQueueId == queueId)
            .OrderBy(item => item.Quality)
            .ToListAsync();
    }

    /// <summary>Get all items for a specific photo</summary>
    public async Task<IEnumerable<ProcessingQueueItem>> GetByPhotoIdAsync(Guid photoId)
    {
        return await _dbSet
            .Where(item => item.PhotoId == photoId)
            .OrderBy(item => item.Quality)
            .ToListAsync();
    }

    /// <summary>Mark an item as completed</summary>
    public async Task MarkCompleteAsync(Guid itemId)
    {
        var item = await _dbSet.FindAsync(itemId);
        if (item != null)
        {
            item.Status = ProcessingStatus.Complete;
            item.CompletedAt = DateTime.UtcNow;
            item.UpdatedAt = DateTime.UtcNow;
            item.LeaseExpiresAt = null;
            _dbSet.Update(item);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>Mark an item as failed with an error message</summary>
    public async Task MarkFailedAsync(Guid itemId, string errorMessage)
    {
        var item = await _dbSet.FindAsync(itemId);
        if (item != null)
        {
            item.Status = ProcessingStatus.Error;
            item.LastError = errorMessage;
            item.UpdatedAt = DateTime.UtcNow;
            item.LeaseExpiresAt = null;
            _dbSet.Update(item);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Atomically claim a batch of work via the DB-level lease. On SQL Server uses the
    /// <c>UPDATE TOP (N) ... OUTPUT inserted.Id ...</c> pattern so the SELECT + UPDATE is
    /// one statement and two workers (in-instance or cross-instance) cannot pick the same
    /// row. On Sqlite / InMemory falls back to a select-then-update (sufficient for tests
    /// and single-instance dev). Reference: Phase 4 scope §4.
    /// </summary>
    public async Task<IReadOnlyList<ProcessingQueueItem>> LeaseNextBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
            return Array.Empty<ProcessingQueueItem>();

        var providerName = _context.Database.ProviderName ?? string.Empty;
        var isSqlServer = providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);

        if (isSqlServer)
        {
                        // SQL Server-only atomic claim with priority ordering. The
            // UPDATE-against-CTE shape lets us combine TOP (@batch) with
            // ORDER BY — plain UPDATE TOP doesn't honour an ORDER BY clause.
            // Priority: Thumbnail -> Medium -> High -> Low -> Watermark, so the
            // user-visible variants (thumbnails for the grid, medium for the
            // modal) land first when a large batch lands together.
            // CreatedAt is the tiebreaker so within a priority bucket the
            // queue still drains FIFO.
            var sql = @"
                ;WITH leasable AS (
                    SELECT TOP (@batch) Id, LeaseExpiresAt
                    FROM ProcessingQueueItems WITH (UPDLOCK, READPAST)
                    WHERE (Status = 0 OR (Status = 3 AND (NextRetryTime IS NULL OR NextRetryTime <= GETUTCDATE())))
                      AND (LeaseExpiresAt IS NULL OR LeaseExpiresAt < GETUTCDATE())
                    ORDER BY
                        CASE Quality
                            WHEN 0 THEN 0
                            WHEN 2 THEN 1
                            WHEN 3 THEN 2
                            WHEN 1 THEN 3
                            WHEN 5 THEN 4
                            ELSE 5
                        END,
                        CreatedAt
                )
                UPDATE leasable
                SET LeaseExpiresAt = DATEADD(SECOND, @leaseSeconds, GETUTCDATE())
                OUTPUT inserted.Id;";

            var ids = await _context.Database
                .SqlQueryRaw<Guid>(
                    sql,
                    new SqlParameter("@batch", batchSize),
                    new SqlParameter("@leaseSeconds", (int)leaseDuration.TotalSeconds))
                .ToListAsync(cancellationToken);

            if (ids.Count == 0)
                return Array.Empty<ProcessingQueueItem>();

            return await _dbSet
                .Where(item => ids.Contains(item.Id))
                .ToListAsync(cancellationToken);
        }

        // Non-SqlServer fallback. Best-effort lease — tests + single-instance Sqlite dev.
        var now = DateTime.UtcNow;
        var leaseUntil = now.Add(leaseDuration);
        var candidates = await _dbSet
            .Where(item =>
                (item.Status == ProcessingStatus.Pending ||
                 (item.Status == ProcessingStatus.Error &&
                  (item.NextRetryTime == null || item.NextRetryTime <= now)))
                && (item.LeaseExpiresAt == null || item.LeaseExpiresAt < now))
            .OrderBy(item => item.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var c in candidates)
        {
            c.LeaseExpiresAt = leaseUntil;
            c.UpdatedAt = now;
        }
        await _context.SaveChangesAsync(cancellationToken);
        return candidates;
    }

    /// <summary>Clear the lease on a single item.</summary>
    public async Task ReleaseLeaseAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await _dbSet.FindAsync(new object[] { itemId }, cancellationToken);
        if (item != null)
        {
            item.LeaseExpiresAt = null;
            item.UpdatedAt = DateTime.UtcNow;
            _dbSet.Update(item);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}

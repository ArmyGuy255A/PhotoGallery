using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Models;

namespace PhotoGallery.Services;

/// <summary>
/// Writes/refreshes <see cref="WorkerHeartbeat"/> rows so the API replica's
/// Service Health dashboard can see workers running on OTHER replicas (the
/// in-memory <see cref="WorkerScheduleRegistry"/> is per-process, so the
/// admin would otherwise only see workers on whichever replica answered
/// the HTTP request — which on our split topology is always the API,
/// which has no workers).
///
/// Each worker calls <see cref="StampAsync"/> on every tick. The row is
/// UPSERTed on (WorkerName, InstanceId), so a worker that's been running
/// for a week occupies exactly one row.
/// </summary>
public class WorkerHeartbeatWriter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorkerHeartbeatWriter> _logger;

    // Per-worker counters kept in process memory so the worker doesn't have
    // to read+update the DB column on every tick (avoids a race when the
    // same worker stamps from multiple code paths). Reset on replica restart.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _totals = new();

    public WorkerHeartbeatWriter(IServiceProvider serviceProvider, ILogger<WorkerHeartbeatWriter> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Atomically bump the per-worker "items processed" counter. Called by
    /// the worker after each successful unit of work (e.g. one photo-version
    /// done). The next <see cref="StampAsync"/> call ships the value to the
    /// DB.
    /// </summary>
    public void IncrementProcessed(string workerName, long delta = 1)
    {
        _totals.AddOrUpdate(workerName, delta, (_, v) => v + delta);
    }

    public async Task StampAsync(
        string workerName,
        string displayName,
        TimeSpan interval,
        DateTime? lastRanAt,
        CancellationToken cancellationToken,
        int itemsInFlight = 0,
        string? lastError = null)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var instanceId = WorkerScheduleRegistry.InstanceId;
            var existing = await db.WorkerHeartbeats
                .FirstOrDefaultAsync(h => h.WorkerName == workerName && h.InstanceId == instanceId, cancellationToken);

            var now = DateTime.UtcNow;
            var processedTotal = _totals.TryGetValue(workerName, out var total) ? total : 0;
            // Truncate to fit the column.
            var safeError = string.IsNullOrEmpty(lastError) || lastError.Length <= 512
                ? lastError
                : lastError.Substring(0, 512);

            if (existing == null)
            {
                db.WorkerHeartbeats.Add(new WorkerHeartbeat
                {
                    Id = Guid.NewGuid(),
                    WorkerName = workerName,
                    InstanceId = instanceId,
                    DisplayName = displayName,
                    IntervalSeconds = Math.Max(1, (int)interval.TotalSeconds),
                    LastHeartbeatAt = now,
                    LastRanAt = lastRanAt,
                    ItemsProcessedTotal = processedTotal,
                    ItemsInFlight = itemsInFlight,
                    LastError = safeError
                });
            }
            else
            {
                existing.DisplayName = displayName;
                existing.IntervalSeconds = Math.Max(1, (int)interval.TotalSeconds);
                existing.LastHeartbeatAt = now;
                if (lastRanAt.HasValue) existing.LastRanAt = lastRanAt;
                existing.ItemsProcessedTotal = processedTotal;
                existing.ItemsInFlight = itemsInFlight;
                // Sticky: a successful tick (lastError == null) clears the
                // previous error; a failing tick overwrites it. So the
                // column always reflects the most recent state.
                existing.LastError = safeError;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Heartbeat write is best-effort — never let a heartbeat failure
            // kill the worker tick. Log at Debug so noisy DB-down windows
            // don't drown the logs.
            _logger.LogDebug(ex, "Failed to stamp heartbeat for {Worker}", workerName);
        }
    }
}

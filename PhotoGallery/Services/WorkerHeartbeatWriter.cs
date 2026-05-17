using System.Diagnostics;
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
///
/// Replicas are ephemeral by design — KEDA spawns workers under load and ACA
/// kills them when traffic dies. Each <see cref="StampAsync"/> also prunes
/// heartbeat rows older than <see cref="StaleHeartbeatRetention"/> so the
/// table doesn't grow without bound. Rows that are stale but younger than
/// the retention window stay visible (with <c>IsAlive=false</c>) so admins
/// can see "this replica just died".
/// </summary>
public class WorkerHeartbeatWriter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorkerHeartbeatWriter> _logger;

    // Per-worker counters kept in process memory so the worker doesn't have
    // to read+update the DB column on every tick (avoids a race when the
    // same worker stamps from multiple code paths). Reset on replica restart.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _totals = new();

    // Process-level CPU sampling. TotalProcessorTime is cumulative, so we need
    // two samples over a known wall-clock interval to compute %. Snapshot the
    // last sample once per process — heartbeats fire from multiple worker
    // threads but the underlying process counters are shared, so a single
    // pair is correct (each worker sees the system-wide delta since the last
    // heartbeat from any worker on this replica).
    private static readonly object _cpuLock = new();
    private static TimeSpan? _lastCpuTime;
    private static DateTime? _lastCpuSampleAt;
    private static readonly int _processorCount = Environment.ProcessorCount;

    // How long an unresponsive replica's heartbeat row sticks around before
    // we prune it. See WorkerHeartbeatThresholds for the policy explanation.
    public static TimeSpan StaleHeartbeatRetention => WorkerHeartbeatThresholds.PruneAfter;

    // Throttle the prune so we're not deleting on every single tick across
    // every worker. One prune per replica per minute is plenty.
    private static DateTime _lastPruneAt = DateTime.MinValue;
    private static readonly TimeSpan _pruneInterval = TimeSpan.FromMinutes(1);
    private static readonly object _pruneLock = new();

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

            var (cpuPercent, workingSet, managedHeap) = SampleProcessMetrics(now);

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
                    LastError = safeError,
                    CpuPercent = cpuPercent,
                    WorkingSetBytes = workingSet,
                    ManagedHeapBytes = managedHeap
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
                existing.CpuPercent = cpuPercent;
                existing.WorkingSetBytes = workingSet;
                existing.ManagedHeapBytes = managedHeap;
            }
            await db.SaveChangesAsync(cancellationToken);

            await MaybePruneStaleAsync(db, now, cancellationToken);
        }
        catch (Exception ex)
        {
            // Heartbeat write is best-effort — never let a heartbeat failure
            // kill the worker tick. Log at Debug so noisy DB-down windows
            // don't drown the logs.
            _logger.LogDebug(ex, "Failed to stamp heartbeat for {Worker}", workerName);
        }
    }

    /// <summary>
    /// Sample process CPU% (across all cores) + memory at this instant.
    /// CpuPercent is null on the very first call from this process — we need
    /// two TotalProcessorTime readings to compute a delta.
    /// </summary>
    private static (double? cpuPercent, long workingSet, long managedHeap) SampleProcessMetrics(DateTime now)
    {
        double? cpuPercent = null;
        long workingSet;
        long managedHeap;
        try
        {
            using var p = Process.GetCurrentProcess();
            var currentCpu = p.TotalProcessorTime;
            workingSet = p.WorkingSet64;
            managedHeap = GC.GetTotalMemory(forceFullCollection: false);

            lock (_cpuLock)
            {
                if (_lastCpuTime.HasValue && _lastCpuSampleAt.HasValue)
                {
                    var wallMs = (now - _lastCpuSampleAt.Value).TotalMilliseconds;
                    if (wallMs > 0)
                    {
                        var cpuMs = (currentCpu - _lastCpuTime.Value).TotalMilliseconds;
                        // Divide by processor count so a single-core fully
                        // pegged process reports 100%, not 100*N. Bound to
                        // [0, 100*N] in case clocks drift slightly.
                        var pct = (cpuMs / wallMs) * 100.0 / Math.Max(1, _processorCount);
                        if (pct < 0) pct = 0;
                        if (pct > 100.0) pct = 100.0;
                        cpuPercent = Math.Round(pct, 1);
                    }
                }
                _lastCpuTime = currentCpu;
                _lastCpuSampleAt = now;
            }
        }
        catch
        {
            // Process metrics are best-effort. On unsupported runtimes
            // (rare for .NET on Linux/Windows but possible in restricted
            // sandboxes), return zeros rather than failing the heartbeat.
            workingSet = 0;
            managedHeap = 0;
        }
        return (cpuPercent, workingSet, managedHeap);
    }

    /// <summary>
    /// Best-effort cleanup of long-dead heartbeats. Throttled so only one
    /// prune runs per replica per minute regardless of how many workers
    /// call StampAsync.
    /// </summary>
    private async Task MaybePruneStaleAsync(ApplicationDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        lock (_pruneLock)
        {
            if (now - _lastPruneAt < _pruneInterval) return;
            _lastPruneAt = now;
        }

        var cutoff = now - StaleHeartbeatRetention;
        try
        {
            var stale = await db.WorkerHeartbeats
                .Where(h => h.LastHeartbeatAt < cutoff)
                .ToListAsync(cancellationToken);
            if (stale.Count == 0) return;
            db.WorkerHeartbeats.RemoveRange(stale);
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Pruned {Count} stale worker heartbeat(s) older than {Cutoff:O}",
                stale.Count, cutoff);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to prune stale heartbeats");
        }
    }
}

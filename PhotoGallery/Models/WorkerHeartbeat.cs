using System.ComponentModel.DataAnnotations;

namespace PhotoGallery.Models;

/// <summary>
/// Per-replica per-worker heartbeat row. Workers stamp this on every tick so
/// the API replica's <c>Service Health</c> dashboard can show workers running
/// on OTHER replicas (the in-memory <c>WorkerScheduleRegistry</c> is per-process
/// and invisible across the API/worker split).
///
/// Unique key: (WorkerName, InstanceId). Workers UPSERT their own row; older
/// rows whose <c>LastHeartbeatAt</c> is more than 2 × <c>IntervalSeconds</c>
/// old are treated as dead by the dashboard.
/// </summary>
public class WorkerHeartbeat
{
    public Guid Id { get; set; }

    /// <summary>Worker stable name, e.g. "PhotoProcessing" / "OrphanedBlobReaper".</summary>
    [Required, MaxLength(64)]
    public string WorkerName { get; set; } = string.Empty;

    /// <summary>
    /// Per-replica identifier. On ACA this is the
    /// <c>CONTAINER_APP_REPLICA_NAME</c> env var; locally it's
    /// <c>Environment.MachineName</c>. Distinguishes the same worker running
    /// on multiple replicas after a scale-out.
    /// </summary>
    [Required, MaxLength(128)]
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Human-readable label, copied from <c>WorkerScheduleRegistry</c>.</summary>
    [MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Tick interval in seconds. Dashboard uses 2× this to decide liveness.</summary>
    public int IntervalSeconds { get; set; }

    /// <summary>UTC timestamp of the worker's last heartbeat.</summary>
    public DateTime LastHeartbeatAt { get; set; }

    /// <summary>UTC timestamp of the worker's most recent successful tick (RecordTick).</summary>
    public DateTime? LastRanAt { get; set; }

    /// <summary>
    /// Running total of work items this replica has processed since startup.
    /// For <c>PhotoProcessing</c> that's photo-versions completed; for
    /// <c>OrphanedBlobReaper</c> it's blobs deleted, etc. Reset to 0 when
    /// the replica restarts.
    /// </summary>
    public long ItemsProcessedTotal { get; set; }

    /// <summary>
    /// Size of the batch the worker is currently chewing on (the most recent
    /// tick's leased count). 0 between ticks. Lets the dashboard show
    /// "12 in flight" right now so admins can tell a stuck worker from an
    /// idle one.
    /// </summary>
    public int ItemsInFlight { get; set; }

    /// <summary>
    /// Last exception summary (truncated to 512 chars). Stays populated until
    /// the next successful tick clears it, so it's a sticky breadcrumb for
    /// "what went wrong recently" without needing the App Insights query.
    /// </summary>
    [MaxLength(512)]
    public string? LastError { get; set; }

    /// <summary>
    /// Process CPU usage at heartbeat time, averaged across the interval since
    /// the previous heartbeat from this replica. 0..100*N for an N-vCPU container.
    /// Lets the dashboard tell at a glance whether a worker is CPU-saturated
    /// (which would explain a growing queue). Null until the second heartbeat,
    /// since we need two samples to compute a delta.
    /// </summary>
    public double? CpuPercent { get; set; }

    /// <summary>
    /// Process working set (RAM in MB the OS sees the process using) at
    /// heartbeat time. The fastest signal that a worker is close to ACA's
    /// memory limit — once this approaches the configured request, ACA will
    /// OOMKill the replica.
    /// </summary>
    public long? WorkingSetBytes { get; set; }

    /// <summary>
    /// .NET managed heap size at heartbeat time. Together with
    /// <see cref="WorkingSetBytes"/> tells you whether memory pressure is
    /// from GC-tracked allocations (e.g. ImageSharp buffers) or native /
    /// unmanaged ones (e.g. raw blob streams).
    /// </summary>
    public long? ManagedHeapBytes { get; set; }
}

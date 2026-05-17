namespace PhotoGallery.Services;

/// <summary>
/// Single source of truth for the time windows the Service Health dashboard
/// uses to grade a worker replica's liveness.
///
/// <para>
/// Three states for a heartbeat row:
/// <list type="bullet">
///   <item><b>Alive</b> — last heartbeat within <see cref="OfflineAfter"/>. Worker is ticking.</item>
///   <item><b>Offline</b> — older than <see cref="OfflineAfter"/> but younger than <see cref="PruneAfter"/>.
///         Still visible on the dashboard with <c>IsAlive=false</c>, so admins can see
///         "this replica just died" before it disappears.</item>
///   <item><b>Gone</b> — older than <see cref="PruneAfter"/>. Row is deleted on the next
///         heartbeat from any replica and never shows again.</item>
/// </list>
/// </para>
/// </summary>
public static class WorkerHeartbeatThresholds
{
    /// <summary>
    /// How long after the most recent heartbeat a worker is still considered
    /// alive. Sized to absorb a single missed tick on the slowest worker
    /// (PhotoVersionUrlRefresh ticks every 60s, others 5-30s) plus DB write
    /// jitter. After this window the replica is shown as Offline (red dot)
    /// but still visible.
    /// </summary>
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long an offline heartbeat row sticks around before it's pruned
    /// from the table. Long enough that a quick replica restart or a brief
    /// DB hiccup doesn't lose history; short enough that an hour of KEDA
    /// scaling churn doesn't pile up dozens of ghost rows.
    /// </summary>
    public static readonly TimeSpan PruneAfter = TimeSpan.FromMinutes(30);
}

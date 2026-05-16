using System.Collections.Concurrent;
using PhotoGallery.Controllers;

namespace PhotoGallery.Services;

/// <summary>
/// Singleton registry that every background worker registers itself with on
/// startup so the admin Service Health page can list "what's running, when
/// did it last tick, when does it tick next" and trigger an immediate run.
///
/// Workers call <see cref="Register"/> from their <c>ExecuteAsync</c> entry
/// point and <see cref="RecordTick"/> on each tick. The trigger plumbing
/// (resetting the worker's internal timer) is implemented by the worker
/// itself via a <see cref="ManualResetEventSlim"/>-style hook.
/// </summary>
public class WorkerScheduleRegistry
{
    private readonly ConcurrentDictionary<string, WorkerEntry> _workers = new();

    /// <summary>
    /// Per-process replica identifier, sampled once. ACA injects
    /// CONTAINER_APP_REPLICA_NAME; locally we fall back to MachineName.
    /// </summary>
    public static readonly string InstanceId =
        Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME") ?? Environment.MachineName;

    public void Register(string name, string displayName, TimeSpan interval, Action? triggerHook = null)
    {
        _workers[name] = new WorkerEntry
        {
            Name = name,
            DisplayName = displayName,
            Interval = interval,
            TriggerHook = triggerHook,
            LastRanAt = null,
            NextRunAt = DateTime.UtcNow.Add(interval)
        };
    }

    public void RecordTick(string name)
    {
        if (_workers.TryGetValue(name, out var entry))
        {
            entry.LastRanAt = DateTime.UtcNow;
            entry.NextRunAt = DateTime.UtcNow.Add(entry.Interval);
        }
    }

    /// <summary>
    /// Returns true if the worker is registered AND exposes a trigger hook.
    /// The trigger hook is expected to wake the worker's internal periodic
    /// timer so the next tick fires immediately.
    /// </summary>
    public bool Trigger(string name)
    {
        if (!_workers.TryGetValue(name, out var entry)) return false;
        if (entry.TriggerHook == null) return false;
        try { entry.TriggerHook.Invoke(); }
        catch { return false; }
        // Optimistic: record an immediate next-run hint so the UI updates
        // before the worker reports back.
        entry.NextRunAt = DateTime.UtcNow.AddSeconds(1);
        return true;
    }

    public List<WorkerStatusDto> Snapshot() => _workers.Values
        .Select(w => new WorkerStatusDto
        {
            Name = w.Name,
            DisplayName = w.DisplayName,
            Interval = w.Interval,
            LastRanAt = w.LastRanAt,
            NextRunAt = w.NextRunAt,
            CanTrigger = w.TriggerHook != null
        })
        .OrderBy(w => w.DisplayName)
        .ToList();

    private sealed class WorkerEntry
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public TimeSpan Interval { get; set; }
        public DateTime? LastRanAt { get; set; }
        public DateTime? NextRunAt { get; set; }
        public Action? TriggerHook { get; set; }
    }
}

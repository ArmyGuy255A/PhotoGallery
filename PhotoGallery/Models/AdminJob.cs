using System.ComponentModel.DataAnnotations;

namespace PhotoGallery.Models;

/// <summary>
/// Persistent work-item the admin UI enqueues so a worker replica picks it
/// up and runs it, instead of the API replica burning its CPU on a long
/// synchronous job.
///
/// Pattern: admin endpoint INSERTs a row with <see cref="Status"/> =
/// <c>"pending"</c> + returns 202 Accepted with the row's <see cref="Id"/>.
/// The relevant worker (<c>StorageConsistencyWorker</c> /
/// <c>OrphanedBlobReaperWorker</c>) polls for pending rows at the top of
/// each tick, claims one atomically, runs the corresponding action, and
/// writes the result back. Admin polls <c>GET /api/photos/admin/jobs/{id}</c>
/// for status.
///
/// Why a small dedicated table instead of <see cref="ProcessingQueueItem"/>:
///   - Different shape (no PhotoId, no Quality, no retry curve).
///   - Different consumer (admin worker, not image-resize worker).
///   - Different lifecycle (one row per admin click, not per photo variant).
/// </summary>
public class AdminJob
{
    public Guid Id { get; set; }

    /// <summary>
    /// Action to run. Constants live in <see cref="AdminJobTypes"/>.
    /// </summary>
    [Required, MaxLength(64)]
    public string JobType { get; set; } = string.Empty;

    /// <summary>
    /// Optional album scope. Only set for per-album reconcile jobs;
    /// null for the global reconcile + reap-orphans paths.
    /// </summary>
    public Guid? AlbumId { get; set; }

    [Required, MaxLength(32)]
    public string Status { get; set; } = AdminJobStatuses.Pending;

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? RequestedBy { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Replica that claimed and ran the job. Useful when the dashboard
    /// shows ten parallel workers and you need to know which one handled
    /// this job.
    /// </summary>
    [MaxLength(128)]
    public string? CompletedByInstanceId { get; set; }

    /// <summary>
    /// JSON-encoded ConsistencyReport / OrphanReapReport / similar — the
    /// shape mirrors what the synchronous endpoint used to return inline.
    /// Null while pending/running.
    /// </summary>
    public string? ResultJson { get; set; }

    /// <summary>Short error message if the job failed. Null on success.</summary>
    [MaxLength(2048)]
    public string? ErrorMessage { get; set; }
}

public static class AdminJobTypes
{
    public const string ReconcileStorage      = "reconcile-storage";
    public const string ReconcileAlbumStorage = "reconcile-album-storage";
    public const string ReapOrphans           = "reap-orphans";

    /// <summary>
    /// CHAOS / dev-only. Randomly deletes blobs from storage to manufacture
    /// inconsistency — used to validate that reconcile + reap recover
    /// correctly. Targets originals AND derived versions indiscriminately.
    /// Guarded by appsettings: only enabled when Development:ChaosEnabled=true.
    /// </summary>
    public const string ChaosStorage          = "chaos-storage";
}

public static class AdminJobStatuses
{
    public const string Pending  = "pending";
    public const string Running  = "running";
    public const string Complete = "complete";
    public const string Error    = "error";
}

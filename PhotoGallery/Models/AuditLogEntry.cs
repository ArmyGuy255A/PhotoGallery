namespace PhotoGallery.Models;

/// <summary>
/// Immutable server-side audit log entry. Records security-relevant actions such as
/// account deletions, role changes, and data exports.
///
/// Audit log rows are NEVER deleted (they record deletions and other compliance events).
/// ActorEmail is denormalized so "who deleted whom" remains queryable after the actor
/// User row itself has been hard-deleted (e.g., GDPR right-to-be-forgotten).
/// </summary>
public class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UTC timestamp when the action occurred.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The acting user's id (nullable: action may have been performed by an anonymous
    /// caller or a now-deleted user — denormalized email below remains).
    /// </summary>
    public string? ActorUserId { get; set; }

    /// <summary>
    /// Denormalized actor email captured at the time of the action. Required so the
    /// audit trail survives hard-deletion of the User row.
    /// </summary>
    public string ActorEmail { get; set; } = string.Empty;

    /// <summary>Action identifier, e.g. "user.deleted", "user.deletion.storage-pending".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Optional type name of the target entity, e.g. "User", "Album".</summary>
    public string? TargetType { get; set; }

    /// <summary>Optional id of the target entity (string for flexibility — Guid or User.Id).</summary>
    public string? TargetId { get; set; }

    /// <summary>Optional JSON blob with action-specific details.</summary>
    public string? Details { get; set; }
}

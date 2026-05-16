namespace PhotoGallery.Models;

public class AccessCode
{
    public Guid Id { get; set; }

    /// <summary>
    /// Album this code unlocks. Nullable: when the album is hard-deleted by
    /// <c>AlbumsController.DeleteAlbum</c> the FK cascades to <c>SetNull</c>
    /// so the code row survives for analytics. The original album name is
    /// snapshotted into <see cref="DeletedAlbumTitle"/> in the same step.
    /// </summary>
    public Guid? AlbumId { get; set; }

    public string Code { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ExpirationDate { get; set; }
    public byte[] RowVersion { get; set; } = new byte[] { 1 };

    /// <summary>
    /// Soft-delete flag. Set to <c>true</c> when the underlying album is
    /// deleted so the code disappears from the shared-album and code-validate
    /// flows while the row survives for admin analytics
    /// (UserAccessLog references etc.).
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>Timestamp the code was soft-deleted (paired with <see cref="IsDeleted"/>).</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Snapshot of <c>Album.Title</c> taken at the moment the album was
    /// deleted, so admin analytics can still show a meaningful name after
    /// the album row is gone.
    /// </summary>
    public string? DeletedAlbumTitle { get; set; }

    // Navigation properties
    public virtual Album? Album { get; set; }
    public virtual ICollection<UserAccessLog> UserAccessLogs { get; set; } = new List<UserAccessLog>();
}

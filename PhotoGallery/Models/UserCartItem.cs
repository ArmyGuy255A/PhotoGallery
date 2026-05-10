using PhotoGallery.Enums;

namespace PhotoGallery.Models;

/// <summary>
/// EPIC May 2026 / Bug #9 — per-user, database-backed shopping cart row.
///
/// One row per (UserId, PhotoId, Quality) — the unique index makes "add" idempotent.
/// Cart items are NOT scoped to a single album: a user can collect photos from
/// multiple albums (own albums + albums unlocked via saved access codes) and
/// check out in a single ZIP.
///
/// <see cref="SourceAlbumId"/> is the album the user added the item from. It is
/// kept as an unenforced soft-FK with <c>OnDelete(SetNull)</c> so deleting an album
/// never blocks. The cart-list UI uses it for grouping; downstream authorization
/// re-derives the album from <see cref="Photo.AlbumId"/>.
/// </summary>
public class UserCartItem
{
    public Guid Id { get; set; }

    /// <summary>
    /// FK to AspNetUsers (Identity User). Stored as <c>string</c> to match
    /// <see cref="SavedAccessCode.UserId"/> and the rest of the codebase —
    /// ASP.NET Identity uses string keys. Spec called for Guid; we follow the
    /// existing convention to keep all user-owned entities consistent.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>FK to <see cref="Photo"/>.</summary>
    public Guid PhotoId { get; set; }

    /// <summary>
    /// Requested download quality. <see cref="QualityType.Thumbnail"/> is rejected
    /// at the controller layer (preview-only).
    /// </summary>
    public QualityType Quality { get; set; }

    /// <summary>
    /// Album the user added the item from (used for cart-UI grouping). Soft FK —
    /// <c>OnDelete(SetNull)</c> so deleting an album does not block. Nullable
    /// because some flows (e.g., admin add) may not have a source album.
    /// </summary>
    public Guid? SourceAlbumId { get; set; }

    /// <summary>UTC DateTime the item was added to the cart.</summary>
    public DateTime AddedAt { get; set; }

    // Navigation properties
    public virtual User? User { get; set; }
    public virtual Photo? Photo { get; set; }
    public virtual Album? SourceAlbum { get; set; }
}

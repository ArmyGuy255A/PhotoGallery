namespace PhotoGallery.Models;

/// <summary>
/// Link table: a registered user has saved an AccessCode to their account.
/// EPIC-02 Slice B — lets authenticated users revisit shared albums from /shared-albums
/// without re-typing the access code. Idempotent: at most one row per (UserId, AccessCodeId).
/// </summary>
public class SavedAccessCode
{
    public Guid Id { get; set; }

    /// <summary>FK to AspNetUsers (Identity User).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>FK to AccessCodes.</summary>
    public Guid AccessCodeId { get; set; }

    /// <summary>UTC DateTime the user saved this code.</summary>
    public DateTime SavedAt { get; set; }

    public virtual User? User { get; set; }
    public virtual AccessCode? AccessCode { get; set; }
}

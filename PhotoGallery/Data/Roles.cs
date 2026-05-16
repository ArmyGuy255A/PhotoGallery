namespace PhotoGallery.Data;

public abstract class Roles
{
    public const string Admin = nameof(Admin);

    /// <summary>
    /// Implicit baseline role — every authenticated user has it. Used by
    /// generic <c>[Authorize(Roles="User")]</c> guards. The admin UI does
    /// not expose a toggle for this role: removing it would orphan a user
    /// into an "elevated-only" state that nothing else in the system
    /// expects.
    /// </summary>
    public const string User = nameof(User);

    /// <summary>
    /// Elevated role granted to users who need to create albums and upload
    /// photos but not other admin powers. Album owners with this role can
    /// access /albums/create, /albums/{id}/photos (upload), and the
    /// per-album reconcile button. Admins automatically have this too —
    /// every admin endpoint accepts <c>"Admin,AlbumCreator"</c>.
    /// </summary>
    public const string AlbumCreator = nameof(AlbumCreator);
}
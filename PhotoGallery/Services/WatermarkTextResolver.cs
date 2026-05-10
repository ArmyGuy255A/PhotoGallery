using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;

namespace PhotoGallery.Services;

/// <summary>
/// Resolves the watermark text for a photo from its uploader's identity. Decoupled from
/// <see cref="WatermarkService"/> (which is a singleton renderer with no DB access) so
/// that <c>ImageProcessingService</c> and <c>PhotoVersionUrlService</c> can reuse the
/// same fallback chain without duplicating it.
///
/// Fallback chain lives in <see cref="WatermarkService.FormatDisplayName"/>; this resolver
/// just looks up the <c>Users</c> row by id and delegates.
///
/// Reference: bug — pre-fix code was rendering the raw <c>Photo.UploadedBy</c> GUID into
/// every watermarked variant. PRs #47 / #48 + EPIC May 2026.
/// </summary>
public interface IWatermarkTextResolver
{
    /// <summary>
    /// Resolve the "© ..." watermark text for the given <c>Photo.UploadedBy</c> value.
    /// </summary>
    /// <param name="uploadedBy">The <c>Photo.UploadedBy</c> field — typically a User.Id (GUID string).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>"© {display name}" or "© Photo Gallery" when no user can be resolved.</returns>
    Task<string> ResolveAsync(string? uploadedBy, CancellationToken ct = default);
}

/// <summary>
/// EF Core-backed implementation. Scoped lifetime — safe to share within a single
/// processing scope; use the optional cache parameter on <see cref="ResolveAsync"/>
/// to avoid repeat lookups when generating multiple quality variants for the same photo.
/// </summary>
public class WatermarkTextResolver : IWatermarkTextResolver
{
    private readonly ApplicationDbContext _db;

    public WatermarkTextResolver(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<string> ResolveAsync(string? uploadedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uploadedBy))
        {
            return "© Photo Gallery";
        }

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == uploadedBy, ct);

        return WatermarkService.FormatDisplayName(user);
    }
}

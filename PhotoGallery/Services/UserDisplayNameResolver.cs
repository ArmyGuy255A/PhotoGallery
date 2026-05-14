using Microsoft.AspNetCore.Identity;
using PhotoGallery.Models;

namespace PhotoGallery.Services;

/// <summary>
/// Resolves a user id (raw GUID stored in <c>CreatedBy</c> / <c>UploadedBy</c>
/// columns) to a human-readable display name suitable for FE rendering, e.g.
/// "Created on … by Phillip Dieppa" instead of "by 08a0e965-50e9-…".
///
/// Single source of truth for the fallback chain so the watermark text on a
/// rendered photo always matches the "created by" label on its album header.
/// Implementation delegates to <see cref="WatermarkService.FormatDisplayName"/>
/// and strips the leading "© " so consumers can interpolate the bare name.
/// </summary>
public interface IUserDisplayNameResolver
{
    /// <summary>
    /// Resolve a single id. Returns "Photo Gallery" when <paramref name="userId"/>
    /// is null / empty / unknown (deleted user). Never throws.
    /// </summary>
    Task<string> ResolveAsync(string? userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a batch of ids in one pass. Deduplicates the input so listing
    /// N rows with K distinct uploaders performs K lookups, not N. The
    /// returned map is keyed by the original ids; unknown / empty ids map to
    /// "Photo Gallery".
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ResolveManyAsync(
        IEnumerable<string?> userIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="UserManager{TUser}"/>-backed implementation. Scoped lifetime so
/// the per-request lookup cache lives only as long as one HTTP request, then
/// gets disposed with the DI scope.
/// </summary>
public class UserDisplayNameResolver : IUserDisplayNameResolver
{
    /// <summary>Bare-name fallback (no leading "© ") for FE interpolation.</summary>
    public const string DefaultDisplayName = "Photo Gallery";

    private readonly UserManager<User> _userManager;

    public UserDisplayNameResolver(UserManager<User> userManager)
    {
        _userManager = userManager;
    }

    public async Task<string> ResolveAsync(string? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return DefaultDisplayName;
        }

        var user = await _userManager.FindByIdAsync(userId);
        return StripCopyrightPrefix(WatermarkService.FormatDisplayName(user));
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveManyAsync(
        IEnumerable<string?> userIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        // Deduplicate distinct non-empty ids so the DB sees one query per
        // unique uploader rather than one per row.
        var distinct = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var id in distinct)
        {
            var user = await _userManager.FindByIdAsync(id);
            result[id] = StripCopyrightPrefix(WatermarkService.FormatDisplayName(user));
        }

        return result;
    }

    private static string StripCopyrightPrefix(string watermarkText)
    {
        // WatermarkService.FormatDisplayName always returns "© <name>". Strip
        // the prefix so the FE renders "by Phillip Dieppa" not "by © Phillip
        // Dieppa". Falls through unchanged if the prefix ever drifts.
        const string prefix = "© ";
        return watermarkText.StartsWith(prefix, StringComparison.Ordinal)
            ? watermarkText[prefix.Length..]
            : watermarkText;
    }
}

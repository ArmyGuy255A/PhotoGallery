namespace PhotoGallery.Services;

/// <summary>
/// Static catalogue of every admin-editable runtime setting. The admin page
/// reads this list at startup and renders one form field per entry. Each
/// entry knows its data type (used to validate updates) and whether
/// changing it requires a process restart to take effect.
///
/// Adding a new editable setting is one line here plus reading it through
/// <see cref="ISettingsResolver"/> at the consumer site.
/// </summary>
public static class SettingsCatalogue
{
    public sealed record SettingCatalogueEntry(
        string Key,
        string Category,
        string DataType,
        string DefaultValue,
        string Description,
        bool RestartRequired)
    {
        public bool IsValid(string value, out string error)
        {
            error = string.Empty;
            switch (DataType)
            {
                case "int":
                    if (!int.TryParse(value, out _))
                    {
                        error = $"'{value}' is not a valid integer.";
                        return false;
                    }
                    return true;
                case "bool":
                    if (!bool.TryParse(value, out _))
                    {
                        error = $"'{value}' is not 'true' or 'false'.";
                        return false;
                    }
                    return true;
                case "double":
                    if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                    {
                        error = $"'{value}' is not a valid decimal number.";
                        return false;
                    }
                    return true;
                default:
                    return true;
            }
        }
    }

    public static IEnumerable<SettingCatalogueEntry> GetAll() => Items;

    private static readonly SettingCatalogueEntry[] Items = new[]
    {
        new SettingCatalogueEntry(
            "PhotoProcessing:IntervalSeconds", "Processing", "int", "5",
            "Tick interval for the background image-processing worker. Lower values drain the queue faster but use more CPU. Hot-reload — takes effect on the next tick.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "PhotoProcessing:WorkerParallelism", "Processing", "int", "5",
            "Max concurrent image-resize consumers per worker tick. On a 0.5 vCPU ACA instance, dropping this to 2 prevents the worker from starving the API of CPU during a bulk upload (the cause of 503s seen during 400-photo uploads). The DB-level lease keeps cross-replica safety. Hot-reload — takes effect on the next tick.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "PhotoProcessing:LeaseBatchMultiplier", "Processing", "int", "4",
            "Number of queue items leased per tick is WorkerParallelism × this multiplier. Lower values reduce DB pressure but underfeed consumers; raise only if workers are sitting idle waiting for the next lease. Hot-reload — takes effect on the next tick.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "Storage:OrphanReapIntervalHours", "Storage", "int", "6",
            "Hours between orphaned-blob reap passes. Hot-reload — takes effect on the next sleep cycle.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "Storage:OrphanReapGraceMinutes", "Storage", "int", "60",
            "Grace window (minutes) — blobs younger than this are skipped by the reaper to protect in-flight direct uploads. Hot-reload — takes effect on the next reap pass.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "PhotoProcessing:ConsistencyCheckEnabled", "Processing", "bool", "true",
            "Kill switch for the storage ↔ DB consistency reconciliation worker. Hot-reload — disabling skips ticks but keeps the loop running so re-enabling resumes on the next tick.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "PhotoProcessing:ConsistencyCheckIntervalHours", "Processing", "int", "1",
            "Hours between consistency-check sweeps. Hot-reload — takes effect on the next sleep cycle.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "Storage:OrphanReapEnabled", "Storage", "bool", "true",
            "Kill switch for the orphaned-blob reaper. Hot-reload — disabling skips ticks but keeps the loop running.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "BlobStorage:PreSignedUrlTTLDays", "Storage", "int", "7",
            "Lifetime of pre-signed public download URLs in days. Hot-reload — takes effect on the next URL generation.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "BlobStorage:PreSignedUrlRefreshWindowDays", "Storage", "int", "5",
            "When a pre-signed URL is within this many days of expiring, the background refresh worker rotates it. Hot-reload — takes effect on the next refresh cycle.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "BlobStorage:RefreshWorkerIntervalHours", "Storage", "int", "24",
            "Tick interval for the pre-signed-URL refresh worker. Hot-reload — takes effect on the next sleep cycle.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "BlobStorage:UrlCacheSlidingMinutes", "Storage", "int", "30",
            "Sliding in-process cache TTL for short-lived pre-signed URLs (thumbnails / watermarked variants served to the public code-gallery). Each access extends the entry by this many minutes, but the entry is hard-capped at the underlying SAS expiry minus a 3-minute safety margin so we never hand out an expired URL. Hot-reload — takes effect on the next cache miss.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "BlobStorage:PublicUrlTtlMinutes", "Storage", "int", "60",
            "TTL (minutes) for the short-lived pre-signed URLs the code-gallery hands to public visitors. Must be larger than UrlCacheSlidingMinutes so the in-process cache has headroom to extend on repeat hits. Hot-reload — takes effect on the next URL sign.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "BlobStorage:VerifyCachedUrls", "Storage", "bool", "false",
            "If true, GetPhotoVersionUrlAsync HEAD-checks each cached URL before reuse — safer but slower. Default is false so paged album rendering doesn't pay N HEAD-request round-trips per page; StorageConsistencyService catches drift on its sweep instead. Hot-reload — takes effect on the next URL fetch.",
            RestartRequired: false),

        // ---------------------------------------------------------------
        // Chaos engineering — Trial only. The Chaos:Enabled kill switch is
        // explicit-off in appsettings.Production.json AND guarded at the
        // controller edge with a 403, so even toggling these via the admin
        // page can't enable chaos in prod without a redeploy + env-var
        // override. Tunables below let you scale the destructiveness for
        // different test scenarios without code changes.
        // ---------------------------------------------------------------
        new SettingCatalogueEntry(
            "Chaos:Enabled", "Chaos", "bool", "false",
            "Master kill-switch for ChaosStorageService. Must be true for any chaos-storage admin job to run; the service refuses otherwise. Defaults to false everywhere; appsettings.Trial.json overrides to true.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "Chaos:DeleteFraction", "Chaos", "double", "0.10",
            "Fraction of the listed blobs (0..1) to delete per chaos run. 0.10 = 10%. The MaxDeletionsPerRun cap still applies on top of this.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "Chaos:MaxDeletionsPerRun", "Chaos", "int", "50",
            "Hard ceiling on blob deletions per chaos run. Stops a single click from nuking the entire bucket regardless of what DeleteFraction resolves to.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "Chaos:IncludeOriginals", "Chaos", "bool", "true",
            "If true, original.jpg blobs are eligible for deletion. Set false to test recovery of derived versions without losing source files.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "Chaos:IncludeDerivedVersions", "Chaos", "bool", "true",
            "If true, derived quality variants (thumbnail/low/medium/high/watermark) are eligible for deletion.",
            RestartRequired: false),
    };
}

/// <summary>
/// Resolves a setting at runtime by checking the RuntimeSettings DB table
/// first, then falling back to <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
/// Every setting in <see cref="SettingsCatalogue"/> is now hot-reloadable;
/// the workers and scoped services that consume settings resolve them via
/// this interface on each tick / call instead of caching at construction.
/// </summary>
public interface ISettingsResolver
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default);
    Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default);
    Task<double> GetDoubleAsync(string key, double fallback, CancellationToken cancellationToken = default);
}

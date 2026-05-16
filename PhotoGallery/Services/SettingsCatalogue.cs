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
            "Tick interval for the background image-processing worker. Lower values drain the queue faster but use more CPU.",
            RestartRequired: true),
        new SettingCatalogueEntry(
            "PhotoProcessing:WorkerParallelism", "Processing", "int", "5",
            "Max concurrent image-resize consumers per worker tick. On a 0.5 vCPU ACA instance, dropping this to 2 prevents the worker from starving the API of CPU during a bulk upload (the cause of 503s seen during 400-photo uploads). The DB-level lease keeps cross-replica safety.",
            RestartRequired: true),
        new SettingCatalogueEntry(
            "PhotoProcessing:LeaseBatchMultiplier", "Processing", "int", "4",
            "Number of queue items leased per tick is WorkerParallelism × this multiplier. Lower values reduce DB pressure but underfeed consumers; raise only if workers are sitting idle waiting for the next lease.",
            RestartRequired: true),
        new SettingCatalogueEntry(
            "PhotoProcessing:ConsistencyCheckEnabled", "Processing", "bool", "true",
            "Master switch for the periodic DB ↔ storage consistency sweep.",
            RestartRequired: true),
        new SettingCatalogueEntry(
            "PhotoProcessing:ConsistencyCheckIntervalHours", "Processing", "int", "1",
            "Hours between consistency-check sweeps.",
            RestartRequired: true),
        new SettingCatalogueEntry(
            "Storage:OrphanReapIntervalHours", "Storage", "int", "6",
            "Hours between orphaned-blob reap passes.",
            RestartRequired: true),
        new SettingCatalogueEntry(
            "Storage:OrphanReapGraceMinutes", "Storage", "int", "60",
            "Grace window (minutes) — blobs younger than this are skipped by the reaper to protect in-flight direct uploads.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "BlobStorage:PreSignedUrlTTLDays", "Storage", "int", "7",
            "Lifetime of pre-signed public download URLs in days.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "BlobStorage:PreSignedUrlRefreshWindowDays", "Storage", "int", "5",
            "When a pre-signed URL is within this many days of expiring, the background refresh worker rotates it.",
            RestartRequired: false),
        new SettingCatalogueEntry(
            "BlobStorage:RefreshWorkerIntervalHours", "Storage", "int", "24",
            "Tick interval for the pre-signed-URL refresh worker.",
            RestartRequired: true),
        new SettingCatalogueEntry(
            "BlobStorage:VerifyCachedUrls", "Storage", "bool", "true",
            "If true, the refresh worker HEAD-checks each cached URL before reuse — safer but slower.",
            RestartRequired: false),
    };
}

/// <summary>
/// Resolves a setting at runtime by checking the RuntimeSettings DB table
/// first, then falling back to <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
/// Most workers read their interval at construction so admin overrides
/// only take effect on restart (the catalogue entry flags this).
/// </summary>
public interface ISettingsResolver
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default);
    Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default);
}

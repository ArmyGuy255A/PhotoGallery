using System.Text.Json;
using PhotoGallery.Services.Storage;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// CHAOS engineering: deliberately delete random blobs from the storage
/// backend to manufacture DB-storage inconsistency. Validates that the
/// reconciler + reaper actually recover from a degraded state in a
/// controlled environment.
///
/// SAFETY:
///  * Master kill-switch: Chaos:Enabled (default false). Trial overrides
///    to true; Production explicitly pins to false.
///  * Per-run cap: Chaos:MaxDeletionsPerRun (default 50).
///  * Per-run fraction: Chaos:DeleteFraction (default 0.10 = 10%).
///  * Scope flags: Chaos:IncludeOriginals + Chaos:IncludeDerivedVersions
///    (both default true).
///
/// All five settings hot-reload via ISettingsResolver -> RuntimeSettings
/// table, so admins can dial chaos up/down between runs without redeploy.
/// </summary>
public class ChaosStorageService
{
    private readonly IStorageProvider _storage;
    private readonly ISettingsResolver _settings;
    private readonly ILogger<ChaosStorageService> _logger;

    public ChaosStorageService(
        IStorageProvider storage,
        ISettingsResolver settings,
        ILogger<ChaosStorageService> logger)
    {
        _storage = storage;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Synchronous fast-path used by the controller's pre-flight guard.
    /// Returns the LAST RESOLVED value of Chaos:Enabled. The actual run
    /// re-resolves it so a flip happens within seconds.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            // Synchronous wait is fine here - the resolver only hits a single
            // RuntimeSettings row + a fallback IConfiguration lookup.
            return _settings.GetBoolAsync("Chaos:Enabled", false).GetAwaiter().GetResult();
        }
    }

    public async Task<ChaosReport> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var enabled = await _settings.GetBoolAsync("Chaos:Enabled", false, cancellationToken);
        if (!enabled)
        {
            _logger.LogWarning(
                "ChaosStorageService run blocked: Chaos:Enabled is false. " +
                "Flip it on (RuntimeSettings or appsettings) to enable.");
            return new ChaosReport
            {
                Blocked = true,
                Reason = "Chaos:Enabled is false on this environment."
            };
        }

        var fraction = Math.Clamp(
            await _settings.GetDoubleAsync("Chaos:DeleteFraction", 0.10, cancellationToken),
            0.0, 1.0);
        var maxDeletions = Math.Max(1,
            await _settings.GetIntAsync("Chaos:MaxDeletionsPerRun", 50, cancellationToken));
        var includeOriginals = await _settings.GetBoolAsync("Chaos:IncludeOriginals", true, cancellationToken);
        var includeDerived = await _settings.GetBoolAsync("Chaos:IncludeDerivedVersions", true, cancellationToken);

        if (!includeOriginals && !includeDerived)
        {
            _logger.LogWarning(
                "ChaosStorageService: both IncludeOriginals and IncludeDerivedVersions are false; nothing to delete");
            return new ChaosReport
            {
                Blocked = true,
                Reason = "Chaos:IncludeOriginals AND Chaos:IncludeDerivedVersions are both false."
            };
        }

        var prefix = "photogallery/";
        _logger.LogWarning(
            "CHAOS RUN START: fraction={Fraction:P0} cap={Cap} originals={Originals} derived={Derived}",
            fraction, maxDeletions, includeOriginals, includeDerived);

        var keys = (await _storage.ListAsync(prefix)).ToList();
        if (keys.Count == 0)
        {
            _logger.LogInformation("ChaosStorageService: no blobs to chaos. Storage is empty.");
            return new ChaosReport { Blocked = false, ScannedBlobs = 0, DeletedBlobs = 0 };
        }

        // Filter by scope. Originals are stored as "*/original.jpg"; everything
        // else is a derived variant (thumbnail / low / medium / high / watermark).
        var eligible = keys.Where(k =>
        {
            var isOriginal = k.EndsWith("/original.jpg", StringComparison.OrdinalIgnoreCase);
            return isOriginal ? includeOriginals : includeDerived;
        }).ToList();

        if (eligible.Count == 0)
        {
            _logger.LogInformation(
                "ChaosStorageService: no eligible blobs after scope filter (originals={Originals}, derived={Derived})",
                includeOriginals, includeDerived);
            return new ChaosReport { Blocked = false, ScannedBlobs = keys.Count, DeletedBlobs = 0 };
        }

        var rng = new Random();
        var sampleSize = Math.Min(maxDeletions, Math.Max(1, (int)(eligible.Count * fraction)));
        var shuffled = eligible.OrderBy(_ => rng.Next()).Take(sampleSize).ToList();

        var deleted = new List<string>(sampleSize);
        var failed = new List<string>();
        foreach (var key in shuffled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _storage.DeleteAsync(key);
                deleted.Add(key);
                _logger.LogInformation("CHAOS deleted blob: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ChaosStorageService failed to delete {Key}", key);
                failed.Add(key);
            }
        }

        var report = new ChaosReport
        {
            Blocked = false,
            ScannedBlobs = keys.Count,
            EligibleBlobs = eligible.Count,
            DeletedBlobs = deleted.Count,
            FailedDeletions = failed.Count,
            DeleteFraction = fraction,
            MaxDeletionsPerRun = maxDeletions,
            IncludeOriginals = includeOriginals,
            IncludeDerivedVersions = includeDerived,
            SampleDeletedKeys = deleted.Take(20).ToList()
        };
        _logger.LogWarning(
            "CHAOS RUN COMPLETE: scanned={Scanned} eligible={Eligible} deleted={Deleted} failed={Failed}",
            report.ScannedBlobs, report.EligibleBlobs, report.DeletedBlobs, report.FailedDeletions);
        return report;
    }
}

/// <summary>Per-run summary for the chaos engineering admin job.</summary>
public class ChaosReport
{
    public bool Blocked { get; set; }
    public string? Reason { get; set; }
    public int ScannedBlobs { get; set; }
    public int EligibleBlobs { get; set; }
    public int DeletedBlobs { get; set; }
    public int FailedDeletions { get; set; }
    public double DeleteFraction { get; set; }
    public int MaxDeletionsPerRun { get; set; }
    public bool IncludeOriginals { get; set; }
    public bool IncludeDerivedVersions { get; set; }
    public List<string> SampleDeletedKeys { get; set; } = new();
}
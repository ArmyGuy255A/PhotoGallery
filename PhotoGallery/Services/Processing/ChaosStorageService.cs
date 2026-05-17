using System.Text.Json;
using PhotoGallery.Services.Storage;

namespace PhotoGallery.Services.Processing;

/// <summary>
/// CHAOS engineering: deliberately delete random blobs from the storage
/// backend to manufacture DB-storage inconsistency. The point is to give
/// us a fast way to validate that the reconciler + reaper actually recover
/// from a degraded state in a controlled environment.
///
/// SAFETY: Only runs when Development:ChaosEnabled=true; capped at
/// MaxBlobsDeletedPerRun per run.
/// </summary>
public class ChaosStorageService
{
    private const int MaxBlobsDeletedPerRun = 50;
    private const double DefaultDeleteFraction = 0.10;

    private readonly IStorageProvider _storage;
    private readonly IConfiguration _config;
    private readonly ILogger<ChaosStorageService> _logger;

    public ChaosStorageService(
        IStorageProvider storage,
        IConfiguration config,
        ILogger<ChaosStorageService> logger)
    {
        _storage = storage;
        _config = config;
        _logger = logger;
    }

    public bool IsEnabled =>
        _config.GetValue("Development:ChaosEnabled", false);

    public async Task<ChaosReport> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning(
                "ChaosStorageService run blocked: Development:ChaosEnabled is false. " +
                "Set it to true in appsettings.Development.json to enable.");
            return new ChaosReport
            {
                Blocked = true,
                Reason = "Development:ChaosEnabled is false on this environment."
            };
        }

        var prefix = "photogallery/";
        _logger.LogWarning(
            "CHAOS RUN START: listing blobs under {Prefix} - this will randomly DELETE " +
            "originals + derived versions to manufacture inconsistency",
            prefix);

        var keys = (await _storage.ListAsync(prefix)).ToList();
        if (keys.Count == 0)
        {
            _logger.LogInformation("ChaosStorageService: no blobs to chaos. Storage is empty.");
            return new ChaosReport { Blocked = false, ScannedBlobs = 0, DeletedBlobs = 0 };
        }

        var rng = new Random();
        var sampleSize = Math.Min(MaxBlobsDeletedPerRun, Math.Max(1, (int)(keys.Count * DefaultDeleteFraction)));
        var shuffled = keys.OrderBy(_ => rng.Next()).Take(sampleSize).ToList();

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
            DeletedBlobs = deleted.Count,
            FailedDeletions = failed.Count,
            SampleDeletedKeys = deleted.Take(20).ToList()
        };
        _logger.LogWarning(
            "CHAOS RUN COMPLETE: scanned={Scanned} deleted={Deleted} failed={Failed}",
            report.ScannedBlobs, report.DeletedBlobs, report.FailedDeletions);
        return report;
    }
}

/// <summary>Per-run summary for the chaos engineering admin job.</summary>
public class ChaosReport
{
    public bool Blocked { get; set; }
    public string? Reason { get; set; }
    public int ScannedBlobs { get; set; }
    public int DeletedBlobs { get; set; }
    public int FailedDeletions { get; set; }
    public List<string> SampleDeletedKeys { get; set; } = new();
}
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Storage;

namespace PhotoGallery.Services;

/// <summary>
/// Default <see cref="ICartZipService"/> implementation. Streams directly from
/// blob storage to the output stream (no full-cart memory buffering) and logs
/// each successfully-added photo to the <see cref="Download"/> table.
///
/// IP addresses are SHA256-hashed before persistence so we can detect repeat
/// actors without storing PII.
/// </summary>
public class CartZipService : ICartZipService
{
    /// <summary>
    /// Default upper bound on items in a single bulk download. Admin can
    /// override at runtime via the Cart:MaxItems setting (hot-reload).
    /// Bumped to 99999 (effectively unlimited) until a ranged-selection UI ships — the streaming zip path keeps memory flat regardless of count, so the cap exists purely as a sanity ceiling on a single download request; the streaming
    /// download path keeps memory flat regardless of count.
    /// </summary>
    public const int MaxItemsPerCartConst = 99999;

    public int MaxItemsPerCart => MaxItemsPerCartConst;

    private readonly IStorageProvider _storageProvider;
    private readonly IRepository<Download> _downloadRepository;
    private readonly ILogger<CartZipService> _logger;

    public CartZipService(
        IStorageProvider storageProvider,
        IRepository<Download> downloadRepository,
        ILogger<CartZipService> logger)
    {
        _storageProvider = storageProvider;
        _downloadRepository = downloadRepository;
        _logger = logger;
    }

    public async Task<int> StreamCartZipAsync(
        IReadOnlyList<CartZipItem> items,
        Stream output,
        Guid? accessCodeId,
        string? remoteIp,
        string? userId = null)
    {
        if (items.Count > MaxItemsPerCartConst)
        {
            throw new ArgumentException(
                $"Cart exceeds maximum of {MaxItemsPerCartConst} items.", nameof(items));
        }

        var ipHash = HashIp(remoteIp);
        var added = 0;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // leaveOpen=true so we don't close the caller's response stream
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in items)
            {
                try
                {
                    var storageKey = PhotoVersionUrlService.BuildStorageKey(item.AlbumId, item.PhotoId, item.Quality);

                    if (!await _storageProvider.ExistsAsync(storageKey))
                    {
                        _logger.LogWarning(
                            "Skipping {PhotoId} {Quality}: storage object missing ({Key})",
                            item.PhotoId, item.Quality, storageKey);
                        continue;
                    }

                    var entryName = BuildEntryName(item.FileName, item.Quality, usedNames);
                    var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);

                    await using (var entryStream = entry.Open())
                    await using (var sourceStream = await _storageProvider.DownloadAsync(storageKey))
                    {
                        await sourceStream.CopyToAsync(entryStream);
                    }

                    await _downloadRepository.AddAsync(new Download
                    {
                        Id = Guid.NewGuid(),
                        PhotoId = item.PhotoId,
                        AccessCodeId = accessCodeId,
                        UserId = userId,
                        Quality = item.Quality,
                        DownloadedAt = DateTime.UtcNow,
                        IpHash = ipHash
                    });

                    added++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error adding {PhotoId} {Quality} to cart ZIP — skipping",
                        item.PhotoId, item.Quality);
                    // Continue with remaining items — partial ZIPs are better than no ZIP.
                }
            }
        }

        if (added > 0)
        {
            await _downloadRepository.SaveChangesAsync();
        }

        _logger.LogInformation(
            "Streamed cart ZIP: {Added}/{Requested} items, code={CodeId}",
            added, items.Count, accessCodeId);

        return added;
    }

    private static string BuildEntryName(string originalFileName, QualityType quality, HashSet<string> used)
    {
        var safeName = string.IsNullOrWhiteSpace(originalFileName)
            ? "photo.jpg"
            : SanitizeFileName(originalFileName);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";

        var qualityFolder = quality.ToString();
        var candidate = $"{qualityFolder}/{nameWithoutExt}{ext}";

        var counter = 1;
        while (!used.Add(candidate))
        {
            candidate = $"{qualityFolder}/{nameWithoutExt}_{counter}{ext}";
            counter++;
        }

        return candidate;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (Array.IndexOf(invalid, c) < 0 && c != '/' && c != '\\')
            {
                sb.Append(c);
            }
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? "photo.jpg" : result;
    }

    private static string HashIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(ip);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

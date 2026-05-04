using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;
using PhotoGallery.Services.Storage;

namespace PhotoGallery.Services;

/// <summary>
/// Streams a ZIP archive containing selected photo versions to an output stream.
///
/// Used by the public cart-checkout endpoint and (in the future) admin bulk download.
/// Streams directly from blob storage to the response without buffering full content
/// in memory, so it scales to large carts.
///
/// Each photo added to the ZIP is logged to the <see cref="Download"/> table for
/// analytics and abuse detection. IP addresses are SHA256-hashed before persistence
/// so we can detect repeat actors without storing PII.
/// </summary>
public class ZipDownloadService
{
    /// <summary>Maximum number of items allowed in a single bulk download request.</summary>
    public const int MaxItemsPerCart = 100;

    private readonly IStorageProvider _storageProvider;
    private readonly IRepository<Download> _downloadRepository;
    private readonly IRepository<Photo> _photoRepository;
    private readonly ILogger<ZipDownloadService> _logger;

    public ZipDownloadService(
        IStorageProvider storageProvider,
        IRepository<Download> downloadRepository,
        IRepository<Photo> photoRepository,
        ILogger<ZipDownloadService> logger)
    {
        _storageProvider = storageProvider;
        _downloadRepository = downloadRepository;
        _photoRepository = photoRepository;
        _logger = logger;
    }

    /// <summary>
    /// Stream a ZIP archive of the requested photo versions to <paramref name="output"/>.
    ///
    /// Behavior:
    /// - Skips items whose photos do not exist or do not belong to <paramref name="albumId"/>
    /// - Skips items whose storage object does not exist (logs warning)
    /// - Logs each successfully-added photo to <see cref="Download"/>
    /// - Returns the count of items successfully added to the archive
    /// </summary>
    /// <param name="albumId">Album the request is scoped to (security check)</param>
    /// <param name="accessCodeId">Access code used (null for authenticated owner downloads)</param>
    /// <param name="items">Distinct list of (photoId, quality) — caller is responsible for dedupe</param>
    /// <param name="output">Output stream that ZIP is written to (typically Response.Body)</param>
    /// <param name="remoteIp">Remote IP for analytics (will be hashed)</param>
    /// <returns>Number of items successfully added to the ZIP</returns>
    public async Task<int> StreamCartZipAsync(
        Guid albumId,
        Guid? accessCodeId,
        IReadOnlyList<CartItem> items,
        Stream output,
        string? remoteIp)
    {
        if (items.Count > MaxItemsPerCart)
        {
            throw new ArgumentException(
                $"Cart exceeds maximum of {MaxItemsPerCart} items.", nameof(items));
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
                    var photo = await _photoRepository.GetByIdAsync(item.PhotoId);
                    if (photo == null)
                    {
                        _logger.LogWarning("Skipping {PhotoId}: photo not found", item.PhotoId);
                        continue;
                    }

                    if (photo.AlbumId != albumId)
                    {
                        _logger.LogWarning(
                            "Skipping {PhotoId}: does not belong to album {AlbumId} (security check)",
                            item.PhotoId, albumId);
                        continue;
                    }

                    var storageKey = PhotoVersionUrlService.BuildStorageKey(albumId, photo.Id, item.Quality);

                    if (!await _storageProvider.ExistsAsync(storageKey))
                    {
                        _logger.LogWarning(
                            "Skipping {PhotoId} {Quality}: storage object missing ({Key})",
                            item.PhotoId, item.Quality, storageKey);
                        continue;
                    }

                    var entryName = BuildEntryName(photo.FileName, item.Quality, usedNames);
                    var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);

                    await using (var entryStream = entry.Open())
                    await using (var sourceStream = await _storageProvider.DownloadAsync(storageKey))
                    {
                        await sourceStream.CopyToAsync(entryStream);
                    }

                    // Log the successful download
                    await _downloadRepository.AddAsync(new Download
                    {
                        Id = Guid.NewGuid(),
                        PhotoId = photo.Id,
                        AccessCodeId = accessCodeId,
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
            "Streamed cart ZIP: {Added}/{Requested} items, album={AlbumId}, code={CodeId}",
            added, items.Count, albumId, accessCodeId);

        return added;
    }

    private static string BuildEntryName(string originalFileName, QualityType quality, HashSet<string> used)
    {
        // Sanitize: keep extension, prefix with quality so user can tell qualities apart
        var safeName = string.IsNullOrWhiteSpace(originalFileName)
            ? "photo.jpg"
            : SanitizeFileName(originalFileName);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";

        var qualityFolder = quality.ToString();
        var candidate = $"{qualityFolder}/{nameWithoutExt}{ext}";

        // Handle duplicate names (e.g., user added same photo twice via UI bug)
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
        // Strip path separators and invalid chars; allow letters, digits, _-. and spaces
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

/// <summary>
/// Represents a single photo version requested in a cart download.
/// </summary>
public class CartItem
{
    public Guid PhotoId { get; set; }
    public QualityType Quality { get; set; }
}

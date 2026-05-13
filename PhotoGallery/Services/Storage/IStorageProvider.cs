namespace PhotoGallery.Services.Storage;

/// <summary>
/// Abstraction for cloud storage operations supporting multiple providers (Minio, Azure Blob Storage, etc.)
/// </summary>
public interface IStorageProvider
{
    /// <summary>
    /// Upload a file to storage
    /// </summary>
    /// <param name="key">Unique identifier/path for the file in storage</param>
    /// <param name="fileStream">File content as stream</param>
    /// <param name="contentType">MIME type (e.g., "image/jpeg")</param>
    /// <returns>Full path or URL to the stored file</returns>
    Task<string> UploadAsync(string key, Stream fileStream, string contentType);

    /// <summary>
    /// Download a file from storage as a stream
    /// </summary>
    /// <param name="key">Unique identifier/path of the file</param>
    /// <returns>File content stream</returns>
    /// <exception cref="FileNotFoundException">Thrown if file does not exist</exception>
    Task<Stream> DownloadAsync(string key);

    /// <summary>
    /// Delete a file from storage
    /// </summary>
    /// <param name="key">Unique identifier/path of the file</param>
    /// <returns>True if deletion was successful, false if file didn't exist</returns>
    Task<bool> DeleteAsync(string key);

    /// <summary>
    /// Get a pre-signed URL for accessing the file
    /// </summary>
    /// <param name="key">Unique identifier/path of the file</param>
    /// <param name="expirationMinutes">URL expiration time in minutes (default: 60)</param>
    /// <returns>Pre-signed URL that can be shared with clients</returns>
    Task<string?> GetUrlAsync(string key, int expirationMinutes = 60);

    /// <summary>
    /// Check if a file exists in storage
    /// </summary>
    /// <param name="key">Unique identifier/path of the file</param>
    /// <returns>True if file exists, false otherwise</returns>
    Task<bool> ExistsAsync(string key);

    /// <summary>
    /// List all files with a given prefix
    /// </summary>
    /// <param name="prefix">Prefix to filter files (e.g., "photos/2024/")</param>
    /// <returns>List of file keys matching the prefix</returns>
    Task<IEnumerable<string>> ListAsync(string prefix);

    /// <summary>
    /// Generate a write-only, single-blob pre-signed URL that lets the caller
    /// PUT a single object directly to storage without proxying bytes through
    /// the API. Used by the direct-to-blob upload flow (Phase 2):
    /// the SPA receives this URL from <c>POST /api/photos/albums/{id}/upload-tickets</c>
    /// and PUTs the file contents to it, then notifies the server via
    /// <c>POST /api/photos/{photoId}/upload-complete</c>.
    ///
    /// Implementations:
    /// <list type="bullet">
    ///   <item>Azure: user-delegation SAS with <c>Write|Create</c>, single-blob
    ///         scope (Resource=b), HTTPS-only.</item>
    ///   <item>MinIO: S3 pre-signed PUT URL.</item>
    /// </list>
    /// </summary>
    /// <param name="key">Blob key the upload will land at (e.g. <c>photogallery/{albumId}/{photoId}/original.jpg</c>)</param>
    /// <param name="ttl">Lifetime of the URL — recommend 30 min for direct-to-blob uploads.</param>
    /// <returns>Absolute URL the client can PUT to.</returns>
    Task<string> GenerateWriteSasUrlAsync(string key, TimeSpan ttl);

    /// <summary>
    /// List immediate child "virtual directories" under <paramref name="prefix"/>
    /// using <c>/</c> as a hierarchy delimiter. Returns sub-prefixes only (each
    /// ending in <c>/</c>) — leaf blobs at this level are not returned. Used by
    /// the orphaned-blob reaper (Phase 5) to enumerate albumGuid/ and
    /// photoGuid/ levels without iterating every variant blob.
    /// </summary>
    /// <param name="prefix">Parent prefix (must end with <c>/</c> or be empty).</param>
    Task<IEnumerable<string>> ListSubPrefixesAsync(string prefix);

    /// <summary>
    /// List all blobs under <paramref name="prefix"/> with size and
    /// last-modified metadata. The orphaned-blob reaper uses this to apply the
    /// grace-period filter (skip blobs younger than N minutes to protect
    /// in-flight direct uploads) and to report bytes reclaimed.
    /// </summary>
    Task<IEnumerable<BlobInfo>> ListWithMetadataAsync(string prefix);

    /// <summary>
    /// Delete many blobs in one logical operation. Implementations may batch
    /// internally (Azure supports 256 blobs per request); callers should not
    /// rely on atomicity. Returns the number of blobs successfully deleted.
    /// Already-deleted blobs are treated as success (idempotent — race-safe
    /// across multiple reaper replicas).
    /// </summary>
    Task<int> DeleteManyAsync(IEnumerable<string> keys);
}

/// <summary>
/// Lightweight metadata record returned by <see cref="IStorageProvider.ListWithMetadataAsync"/>.
/// </summary>
public sealed record BlobInfo(string Key, long Size, DateTimeOffset LastModified);

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
}

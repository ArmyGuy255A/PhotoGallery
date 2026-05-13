using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace PhotoGallery.Services.Storage;

/// <summary>
/// Azure Blob Storage provider for production deployments
/// </summary>
public class AzureStorageProvider : IStorageProvider
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<AzureStorageProvider> _logger;

    public AzureStorageProvider(IConfiguration configuration, ILogger<AzureStorageProvider> logger)
    {
        _logger = logger;
        
        var connectionString = configuration["Storage:Azure:ConnectionString"];
        var containerName = configuration["Storage:Azure:ContainerName"] ?? "photogallery";

        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("Storage:Azure:ConnectionString is not configured");

        var blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    public async Task<string> UploadAsync(string key, Stream fileStream, string contentType)
    {
        try
        {
            await _containerClient.CreateIfNotExistsAsync();
            
            var blobClient = _containerClient.GetBlobClient(key);
            
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            };

            await blobClient.UploadAsync(fileStream, overwrite: true);
            
            _logger.LogInformation("File uploaded to Azure Blob Storage: {Key}", key);
            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to Azure Blob Storage: {Key}", key);
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(key);
            
            if (!await blobClient.ExistsAsync())
                throw new FileNotFoundException($"Blob '{key}' not found in Azure Blob Storage");

            var download = await blobClient.DownloadAsync();
            
            _logger.LogInformation("File downloaded from Azure Blob Storage: {Key}", key);
            return download.Value.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from Azure Blob Storage: {Key}", key);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(key);
            
            var result = await blobClient.DeleteIfExistsAsync();
            
            if (result)
                _logger.LogInformation("File deleted from Azure Blob Storage: {Key}", key);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from Azure Blob Storage: {Key}", key);
            return false;
        }
    }

    public async Task<string?> GetUrlAsync(string key, int expirationMinutes = 60)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(key);
            
            if (!await blobClient.ExistsAsync())
                return null;

            var sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTime.UtcNow.AddMinutes(expirationMinutes));
            
            _logger.LogInformation("SAS URL generated for Azure Blob Storage: {Key}", key);
            return sasUri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating SAS URL for Azure Blob Storage: {Key}", key);
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(key);
            return await blobClient.ExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking blob existence in Azure Blob Storage: {Key}", key);
            return false;
        }
    }

    public async Task<IEnumerable<string>> ListAsync(string prefix)
    {
        try
        {
            var items = new List<string>();

            await foreach (var blobItem in _containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
            {
                items.Add(blobItem.Name);
            }

            _logger.LogInformation("Listed {Count} blobs from Azure Blob Storage with prefix: {Prefix}", items.Count, prefix);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing blobs from Azure Blob Storage with prefix: {Prefix}", prefix);
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>
    /// Account-key SAS variant of write-only single-blob URL. Used only by the
    /// legacy <c>Storage:Provider=Azure</c> alias; the AAD-only profile uses
    /// <see cref="AzureBlobStorageProvider.GenerateWriteSasUrlAsync"/>.
    /// </summary>
    public Task<string> GenerateWriteSasUrlAsync(string key, TimeSpan ttl)
    {
        var blobClient = _containerClient.GetBlobClient(key);
        var sasUri = blobClient.GenerateSasUri(
            BlobSasPermissions.Write | BlobSasPermissions.Create,
            DateTimeOffset.UtcNow.Add(ttl));
        return Task.FromResult(sasUri.ToString());
    }

    public async Task<IEnumerable<string>> ListSubPrefixesAsync(string prefix)
    {
        try
        {
            var prefixes = new List<string>();
            await foreach (var page in _containerClient
                .GetBlobsByHierarchyAsync(BlobTraits.None, BlobStates.None, delimiter: "/", prefix: prefix, cancellationToken: CancellationToken.None)
                .AsPages())
            {
                foreach (var item in page.Values)
                {
                    if (item.IsPrefix && !string.IsNullOrEmpty(item.Prefix))
                    {
                        prefixes.Add(item.Prefix);
                    }
                }
            }
            return prefixes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing sub-prefixes from Azure Blob Storage with prefix: {Prefix}", prefix);
            return Enumerable.Empty<string>();
        }
    }

    public async Task<IEnumerable<BlobInfo>> ListWithMetadataAsync(string prefix)
    {
        try
        {
            var items = new List<BlobInfo>();
            await foreach (var blobItem in _containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
            {
                var size = blobItem.Properties.ContentLength ?? 0L;
                var lastModified = blobItem.Properties.LastModified;
                if (lastModified is null)
                {
                    _logger.LogWarning(
                        "Blob metadata for {Key} has null LastModified during prefix listing; defaulting to current UTC time",
                        blobItem.Name);
                }
                items.Add(new BlobInfo(blobItem.Name, size, lastModified ?? DateTimeOffset.UtcNow));
            }
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing blobs with metadata from Azure Blob Storage with prefix: {Prefix}", prefix);
            return Enumerable.Empty<BlobInfo>();
        }
    }

    public async Task<int> DeleteManyAsync(IEnumerable<string> keys)
    {
        var keyList = keys?.ToList() ?? new List<string>();
        if (keyList.Count == 0) return 0;

        int deleted = 0;
        foreach (var key in keyList)
        {
            try
            {
                var resp = await _containerClient.GetBlobClient(key).DeleteIfExistsAsync();
                if (resp.Value) deleted++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DeleteMany: failed to delete blob {Key}", key);
            }
        }
        return deleted;
    }
}

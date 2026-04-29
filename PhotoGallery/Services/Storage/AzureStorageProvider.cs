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
}

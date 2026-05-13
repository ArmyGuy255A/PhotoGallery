using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace PhotoGallery.Services.Storage;

/// <summary>
/// Azure Blob Storage provider built for the Azure-backed PhotoGallery profile.
///
/// Auth model:
/// <list type="bullet">
///   <item>Constructed with a <see cref="BlobServiceClient"/> that uses
///         <c>DefaultAzureCredential</c> (no account keys, no connection strings).
///         This is the only auth model compatible with the Terraform
///         <c>shared_access_key_enabled = false</c> setting on the dev Storage Account.</item>
///   <item>Download URLs (<see cref="GetUrlAsync"/>) are user-delegation SAS,
///         signed with a <see cref="UserDelegationKey"/> fetched from Azure AD
///         on behalf of the service principal and cached by
///         <see cref="IUserDelegationKeyProvider"/>.</item>
/// </list>
///
/// Distinct from the legacy <see cref="AzureStorageProvider"/> (connection-string +
/// account-key SAS), which remains for the <c>Storage:Provider=Azure</c> alias.
/// </summary>
public sealed class AzureBlobStorageProvider : IStorageProvider
{
    private readonly BlobServiceClient _serviceClient;
    private readonly IUserDelegationKeyProvider _udkProvider;
    private readonly string _containerName;
    private readonly ILogger<AzureBlobStorageProvider> _logger;

    public AzureBlobStorageProvider(
        BlobServiceClient serviceClient,
        IUserDelegationKeyProvider udkProvider,
        string containerName,
        ILogger<AzureBlobStorageProvider> logger)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
        _udkProvider = udkProvider ?? throw new ArgumentNullException(nameof(udkProvider));
        _containerName = string.IsNullOrWhiteSpace(containerName)
            ? throw new ArgumentException("Container name is required.", nameof(containerName))
            : containerName;
        _logger = logger;
    }

    private BlobContainerClient Container =>
        _serviceClient.GetBlobContainerClient(_containerName);

    public async Task<string> UploadAsync(string key, Stream fileStream, string contentType)
    {
        try
        {
            await Container.CreateIfNotExistsAsync();
            var blob = Container.GetBlobClient(key);
            var options = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            };
            await blob.UploadAsync(fileStream, options);
            _logger.LogInformation("File uploaded to Azure Blob: {Key}", key);
            return blob.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to Azure Blob: {Key}", key);
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        try
        {
            var blob = Container.GetBlobClient(key);
            if (!await blob.ExistsAsync())
            {
                throw new FileNotFoundException($"Blob '{key}' not found in Azure Blob.");
            }
            var download = await blob.DownloadAsync();
            _logger.LogInformation("File downloaded from Azure Blob: {Key}", key);
            return download.Value.Content;
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            _logger.LogError(ex, "Error downloading file from Azure Blob: {Key}", key);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        try
        {
            var blob = Container.GetBlobClient(key);
            var response = await blob.DeleteIfExistsAsync();
            if (response.Value)
            {
                _logger.LogInformation("File deleted from Azure Blob: {Key}", key);
            }
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from Azure Blob: {Key}", key);
            return false;
        }
    }

    public async Task<string?> GetUrlAsync(string key, int expirationMinutes = 60)
    {
        try
        {
            var blob = Container.GetBlobClient(key);
            if (!await blob.ExistsAsync())
            {
                return null;
            }

            var udk = await _udkProvider.GetAsync();
            var expiresOn = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes);

            var sasUri = BuildBlobSasUri(
                accountName: _serviceClient.AccountName,
                containerName: _containerName,
                blobName: key,
                userDelegationKey: udk,
                expiresOn: expiresOn,
                blobEndpoint: _serviceClient.Uri);

            _logger.LogInformation(
                "User-delegation SAS generated for Azure Blob: {Key} (expires {ExpiresOn}).",
                key,
                expiresOn);
            return sasUri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating user-delegation SAS for Azure Blob: {Key}", key);
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            var blob = Container.GetBlobClient(key);
            return await blob.ExistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking blob existence in Azure Blob: {Key}", key);
            return false;
        }
    }

    public async Task<IEnumerable<string>> ListAsync(string prefix)
    {
        try
        {
            var items = new List<string>();
            await foreach (var blobItem in Container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
            {
                items.Add(blobItem.Name);
            }
            _logger.LogInformation(
                "Listed {Count} blobs from Azure Blob with prefix: {Prefix}",
                items.Count,
                prefix);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing blobs from Azure Blob with prefix: {Prefix}", prefix);
            return Array.Empty<string>();
        }
    }

    public async Task<IEnumerable<string>> ListSubPrefixesAsync(string prefix)
    {
        try
        {
            var prefixes = new List<string>();
            await foreach (var page in Container
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
            _logger.LogError(ex, "Error listing sub-prefixes from Azure Blob with prefix: {Prefix}", prefix);
            return Array.Empty<string>();
        }
    }

    public async Task<IEnumerable<BlobInfo>> ListWithMetadataAsync(string prefix)
    {
        try
        {
            var items = new List<BlobInfo>();
            await foreach (var blobItem in Container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
            {
                var size = blobItem.Properties.ContentLength ?? 0L;
                var lastModified = blobItem.Properties.LastModified ?? DateTimeOffset.UtcNow;
                items.Add(new BlobInfo(blobItem.Name, size, lastModified));
            }
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing blobs with metadata from Azure Blob with prefix: {Prefix}", prefix);
            return Array.Empty<BlobInfo>();
        }
    }

    public async Task<int> DeleteManyAsync(IEnumerable<string> keys)
    {
        var keyList = keys?.ToList() ?? new List<string>();
        if (keyList.Count == 0) return 0;

        int deleted = 0;
        // BlobBatchClient supports up to 256 URIs per request.
        const int BatchSize = 256;
        foreach (var chunk in keyList.Chunk(BatchSize))
        {
            foreach (var key in chunk)
            {
                try
                {
                    var resp = await Container.GetBlobClient(key).DeleteIfExistsAsync();
                    if (resp.Value)
                    {
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    // Idempotent race: treat individual failures as warnings, keep going.
                    _logger.LogWarning(ex, "DeleteMany: failed to delete blob {Key}", key);
                }
            }
        }
        return deleted;
    }

    /// <summary>
    /// Builds a user-delegation SAS URI for the given blob.
    ///
    /// Pure helper — no network calls, no DI — so it's unit-testable with a
    /// synthetic <see cref="UserDelegationKey"/> produced by
    /// <see cref="BlobsModelFactory"/>.
    ///
    /// SAS shape:
    /// <list type="bullet">
    ///   <item><c>Protocol = SasProtocol.Https</c> — explicit, matching the
    ///         protocol gotcha called out in <see cref="MinioStorageProvider"/>.
    ///         Azure blob endpoints are always HTTPS and we lock that in so
    ///         consumers can't be downgraded to HTTP.</item>
    ///   <item><c>Resource = "b"</c> (single blob) with <c>Read</c> permission.</item>
    ///   <item>Signed with the supplied <see cref="UserDelegationKey"/> — the
    ///         resulting SAS includes <c>skoid</c>/<c>sktid</c> claims that
    ///         account-key SAS does not, which is how downstream verification
    ///         distinguishes the two.</item>
    /// </list>
    /// </summary>
    internal static Uri BuildBlobSasUri(
        string accountName,
        string containerName,
        string blobName,
        UserDelegationKey userDelegationKey,
        DateTimeOffset expiresOn,
        Uri blobEndpoint)
    {
        var builder = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName = blobName,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(5)),
            ExpiresOn = expiresOn,
            Protocol = SasProtocol.Https,
        };
        builder.SetPermissions(BlobSasPermissions.Read);

        var sasToken = builder.ToSasQueryParameters(userDelegationKey, accountName).ToString();

        var basePath = blobEndpoint.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        // Escape blob name segment-by-segment so slashes (virtual-directory
        // separators in Azure Blob) remain intact while other reserved
        // characters get properly percent-encoded.
        var escapedBlobName = string.Join('/',
            blobName.Split('/').Select(Uri.EscapeDataString));
        var blobPath = $"{basePath}/{containerName}/{escapedBlobName}";
        return new Uri($"{blobPath}?{sasToken}");
    }
}

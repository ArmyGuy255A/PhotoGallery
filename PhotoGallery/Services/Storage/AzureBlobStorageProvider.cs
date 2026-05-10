namespace PhotoGallery.Services.Storage;

/// <summary>
/// Placeholder Azure Blob Storage provider for the Azure-backed local-dev
/// profile (<c>ASPNETCORE_ENVIRONMENT=DevelopmentAzure</c>).
///
/// The DI shape is intentionally final: <see cref="StorageProviderFactory"/>
/// already constructs it from <c>Storage:AzureBlob:AccountUrl</c> + container name,
/// and downstream code resolves <see cref="IStorageProvider"/> identically
/// regardless of provider. The real implementation (DefaultAzureCredential +
/// BlobServiceClient with RBAC, no connection strings) drops in here without
/// touching <c>Program.cs</c> or the factory.
///
/// Distinct from <see cref="AzureStorageProvider"/>, which uses connection-string
/// auth and is retained for legacy <c>Storage:Provider=Azure</c> wiring.
///
/// Owner: backend dev. Tracked as a follow-up to the Azure-backed dev wiring PR.
/// </summary>
public sealed class AzureBlobStorageProvider : IStorageProvider
{
    private const string PendingMessage =
        "AzureBlobStorageProvider pending — see backend dev. " +
        "Set Storage:Provider=Minio for the all-local stack until the real impl lands.";

    private readonly string _accountUrl;
    private readonly string _containerName;
    private readonly ILogger<AzureBlobStorageProvider> _logger;

    public AzureBlobStorageProvider(
        string accountUrl,
        string containerName,
        ILogger<AzureBlobStorageProvider> logger)
    {
        _accountUrl = accountUrl;
        _containerName = containerName;
        _logger = logger;
        _logger.LogInformation(
            "AzureBlobStorageProvider placeholder constructed (AccountUrl={AccountUrl}, Container={Container}). " +
            "Operations will throw NotImplementedException until the real impl lands.",
            string.IsNullOrEmpty(_accountUrl) ? "<unset>" : _accountUrl,
            _containerName);
    }

    public Task<string> UploadAsync(string key, Stream fileStream, string contentType) =>
        throw new NotImplementedException(PendingMessage);

    public Task<Stream> DownloadAsync(string key) =>
        throw new NotImplementedException(PendingMessage);

    public Task<bool> DeleteAsync(string key) =>
        throw new NotImplementedException(PendingMessage);

    public Task<string?> GetUrlAsync(string key, int expirationMinutes = 60) =>
        throw new NotImplementedException(PendingMessage);

    public Task<bool> ExistsAsync(string key) =>
        throw new NotImplementedException(PendingMessage);

    public Task<IEnumerable<string>> ListAsync(string prefix) =>
        throw new NotImplementedException(PendingMessage);
}

using Amazon;
using Amazon.S3;
using Azure.Identity;
using Azure.Storage.Blobs;

namespace PhotoGallery.Services.Storage;

/// <summary>
/// Factory for creating the appropriate <see cref="IStorageProvider"/> based on configuration.
///
/// Configuration keys (precedence):
/// <list type="bullet">
///   <item><c>Storage:Provider</c> — canonical key. Values: <c>Minio</c>, <c>AzureBlob</c>, <c>Azure</c> (legacy connection-string Azure).</item>
///   <item><c>Storage:Type</c> — legacy fallback retained for backward-compat with existing deployments.</item>
/// </list>
///
/// <c>AzureBlob</c> wires the RBAC + <c>DefaultAzureCredential</c> implementation
/// (<see cref="AzureBlobStorageProvider"/>) that uses user-delegation SAS for
/// presigned download URLs. This is the only auth model compatible with the
/// dev Storage Account's <c>shared_access_key_enabled = false</c> Terraform setting.
/// </summary>
public class StorageProviderFactory
{
    public static IStorageProvider Create(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        // Storage:Provider is canonical; Storage:Type is the legacy alias.
        var storageType = configuration["Storage:Provider"]
                          ?? configuration["Storage:Type"]
                          ?? "Minio";

        return storageType.ToLowerInvariant() switch
        {
            "minio" => CreateMinioProvider(configuration, serviceProvider),
            "azure" => CreateAzureProvider(configuration, serviceProvider),
            "azureblob" => CreateAzureBlobProvider(configuration, serviceProvider),
            _ => throw new InvalidOperationException(
                $"Unknown storage provider: {storageType}. Supported: Minio, AzureBlob, Azure.")
        };
    }

    private static IStorageProvider CreateMinioProvider(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        var endpoint = configuration["Storage:Minio:Endpoint"] ?? "localhost:9000";
        var accessKey = configuration["Storage:Minio:AccessKey"] ?? "minioadmin";
        var secretKey = configuration["Storage:Minio:SecretKey"] ?? "minioadmin";
        var useSSL = configuration.GetValue<bool>("Storage:Minio:UseSSL", false);

        var s3Config = new AmazonS3Config
        {
            ServiceURL = $"http{(useSSL ? "s" : "")}://{endpoint}",
            ForcePathStyle = true,
            UseHttp = !useSSL
        };

        var s3Client = new AmazonS3Client(accessKey, secretKey, s3Config);

        var logger = serviceProvider.GetRequiredService<ILogger<MinioStorageProvider>>();
        return new MinioStorageProvider(s3Client, configuration, logger);
    }

    private static IStorageProvider CreateAzureProvider(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<AzureStorageProvider>>();
        return new AzureStorageProvider(configuration, logger);
    }

    /// <summary>
    /// RBAC + <c>DefaultAzureCredential</c>-based Azure Blob provider used by
    /// the Azure-backed dev profile and (future) Azure production.
    /// Construction is deliberately keyless: no connection string, no shared key.
    /// Presigned download URLs are generated as user-delegation SAS.
    /// </summary>
    private static IStorageProvider CreateAzureBlobProvider(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        var accountUrl = configuration["Storage:AzureBlob:AccountUrl"];
        if (string.IsNullOrWhiteSpace(accountUrl))
        {
            throw new InvalidOperationException(
                "Storage:AzureBlob:AccountUrl is required when Storage:Provider=AzureBlob. " +
                "Set it in appsettings.{Environment}.json or via the Key Vault secret " +
                "'Storage--AzureBlob--AccountUrl' (or override per developer via env var).");
        }

        var containerName = configuration["Storage:AzureBlob:ContainerName"] ?? "photogallery";

        // DefaultAzureCredential transparently picks up:
        //   - `az login` credentials in local dev,
        //   - Managed Identity when running in Azure,
        //   - Workload Identity in AKS.
        // The Storage Account's shared_access_key_enabled is false, so any
        // attempt to use account keys would fail by design.
        var serviceClient = new BlobServiceClient(new Uri(accountUrl), new DefaultAzureCredential());

        var udkLogger = serviceProvider.GetRequiredService<ILogger<CachingUserDelegationKeyProvider>>();
        var udkProvider = new CachingUserDelegationKeyProvider(serviceClient, udkLogger);

        var logger = serviceProvider.GetRequiredService<ILogger<AzureBlobStorageProvider>>();
        return new AzureBlobStorageProvider(serviceClient, udkProvider, containerName, logger);
    }
}

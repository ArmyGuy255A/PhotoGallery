using Amazon;
using Amazon.S3;

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
/// <c>AzureBlob</c> selects a placeholder that throws <see cref="NotImplementedException"/>.
/// The real RBAC + DefaultAzureCredential implementation is pending — see backend dev / pg-architect.
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
            "azureblob" => CreateAzureBlobPlaceholder(configuration, serviceProvider),
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
    /// RBAC + DefaultAzureCredential-based Azure Blob provider, used by the
    /// Azure-backed local-dev profile (<c>ASPNETCORE_ENVIRONMENT=DevelopmentAzure</c>).
    /// Currently a placeholder that throws on use — DI shape is final so the real
    /// implementation drops in without touching <c>Program.cs</c>.
    /// </summary>
    private static IStorageProvider CreateAzureBlobPlaceholder(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<AzureBlobStorageProvider>>();
        var accountUrl = configuration["Storage:AzureBlob:AccountUrl"] ?? string.Empty;
        var containerName = configuration["Storage:AzureBlob:ContainerName"] ?? "photogallery";
        return new AzureBlobStorageProvider(accountUrl, containerName, logger);
    }
}

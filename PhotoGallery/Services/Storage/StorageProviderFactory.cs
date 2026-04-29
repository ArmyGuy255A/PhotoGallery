using Amazon;
using Amazon.S3;

namespace PhotoGallery.Services.Storage;

/// <summary>
/// Factory for creating appropriate storage provider based on configuration
/// </summary>
public class StorageProviderFactory
{
    public static IStorageProvider Create(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        var storageType = configuration["Storage:Type"] ?? "Minio";

        return storageType.ToLowerInvariant() switch
        {
            "minio" => CreateMinioProvider(configuration, serviceProvider),
            "azure" => CreateAzureProvider(configuration, serviceProvider),
            _ => throw new InvalidOperationException($"Unknown storage type: {storageType}. Supported types: minio, azure")
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
}

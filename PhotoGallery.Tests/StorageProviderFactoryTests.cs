using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoGallery.Services.Storage;

namespace PhotoGallery.Tests;

/// <summary>
/// TDD coverage for <see cref="StorageProviderFactory"/>:
/// — `Storage:Provider` is the canonical key (new); falls back to legacy `Storage:Type`.
/// — `Minio` continues to wire the real MinIO/S3 provider.
/// — `AzureBlob` returns a placeholder that throws on use (real impl pending).
/// — Unknown values raise a clear error.
/// </summary>
public class StorageProviderFactoryTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>))
            .BuildServiceProvider();

    [Fact]
    public void Create_DefaultsToMinio_WhenNoProviderConfigured()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var sp = BuildServices();

        var provider = StorageProviderFactory.Create(config, sp);

        Assert.IsType<MinioStorageProvider>(provider);
    }

    [Fact]
    public void Create_HonorsStorageProviderKey_ForMinio()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Minio"
        });

        var provider = StorageProviderFactory.Create(config, BuildServices());

        Assert.IsType<MinioStorageProvider>(provider);
    }

    [Fact]
    public void Create_HonorsLegacyStorageTypeKey_ForBackwardCompat()
    {
        // Legacy key still works so existing deployments don't break.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Storage:Type"] = "Minio"
        });

        var provider = StorageProviderFactory.Create(config, BuildServices());

        Assert.IsType<MinioStorageProvider>(provider);
    }

    [Fact]
    public void Create_AzureBlob_ReturnsPlaceholderProvider()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "AzureBlob",
            ["Storage:AzureBlob:AccountUrl"] = "https://example.blob.core.windows.net/"
        });

        var provider = StorageProviderFactory.Create(config, BuildServices());

        Assert.IsType<AzureBlobStorageProvider>(provider);
    }

    [Fact]
    public async Task AzureBlobPlaceholder_ThrowsNotImplemented_OnUpload()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "AzureBlob",
            ["Storage:AzureBlob:AccountUrl"] = "https://example.blob.core.windows.net/"
        });

        var provider = StorageProviderFactory.Create(config, BuildServices());

        var ex = await Assert.ThrowsAsync<NotImplementedException>(() =>
            provider.UploadAsync("k", new MemoryStream(), "image/png"));
        Assert.Contains("AzureBlobStorageProvider pending", ex.Message);
    }

    [Fact]
    public void Create_UnknownProvider_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "MagicCloud"
        });

        Assert.Throws<InvalidOperationException>(() =>
            StorageProviderFactory.Create(config, BuildServices()));
    }
}

using System.Reflection;
using System.Web;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoGallery.Services.Storage;

namespace PhotoGallery.Tests;

/// <summary>
/// TDD coverage for <see cref="AzureBlobStorageProvider"/>:
/// — Pre-signed URLs are well-formed user-delegation SAS (Protocol=https, sp=r,
///   skoid/sktid present, which are the markers of a user-delegation SAS as
///   opposed to an account-key SAS).
/// — The provider's constructor accepts no IConfiguration and exposes no
///   account-key / connection-string surface, proving it cannot read account
///   keys at runtime. Defense against accidental regression to the legacy
///   <see cref="AzureStorageProvider"/> auth model.
/// — <see cref="CachingUserDelegationKeyProvider"/> caches the first key and
///   does not re-fetch on subsequent calls.
/// </summary>
public class AzureBlobStorageProviderTests
{
    private static UserDelegationKey FakeUserDelegationKey() =>
        BlobsModelFactory.UserDelegationKey(
            "00000000-0000-0000-0000-000000000001", // signedObjectId
            "00000000-0000-0000-0000-000000000002", // signedTenantId
            "b",                                    // signedService
            "2024-11-04",                           // signedVersion
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                "this-is-not-a-real-key-just-bytes-to-sign-with")), // value
            DateTimeOffset.UtcNow.AddMinutes(-5),   // signedStartsOn
            DateTimeOffset.UtcNow.AddDays(7));      // signedExpiresOn

    [Fact]
    public void BuildBlobSasUri_ProducesUserDelegationSasWithExpectedShape()
    {
        // Arrange: a synthetic user-delegation key + the same shape the
        // production code would pass through.
        var udk = FakeUserDelegationKey();
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(30);

        // Act
        var uri = AzureBlobStorageProvider.BuildBlobSasUri(
            accountName: "demoacct",
            containerName: "photogallery",
            blobName: "albums/2026/photo-1.jpg",
            userDelegationKey: udk,
            expiresOn: expiresOn,
            blobEndpoint: new Uri("https://demoacct.blob.core.windows.net/"));

        // Assert: URI shape
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("demoacct.blob.core.windows.net", uri.Host);
        Assert.StartsWith("/photogallery/albums/2026/photo-1.jpg", uri.AbsolutePath);

        // Assert: SAS query parameters
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("b", query["sr"]);           // signed resource = blob
        Assert.Equal("r", query["sp"]);           // signed permissions = read
        Assert.Equal("https", query["spr"]);      // signed protocol = https (gotcha lock)
        Assert.False(string.IsNullOrEmpty(query["sig"]), "expected sig in SAS");
        Assert.False(string.IsNullOrEmpty(query["se"]), "expected se (expires on) in SAS");

        // User-delegation markers — these are absent from account-key SAS, so
        // their presence proves the SAS was signed with the user-delegation key.
        Assert.False(string.IsNullOrEmpty(query["skoid"]),
            "expected skoid (signed key object id) — proves user-delegation SAS, not account-key SAS");
        Assert.False(string.IsNullOrEmpty(query["sktid"]),
            "expected sktid (signed key tenant id) — proves user-delegation SAS, not account-key SAS");
    }

    [Fact]
    public void Constructor_DoesNotAcceptIConfiguration_SoItCannotReadAccountKeys()
    {
        // Regression-guard: the legacy AzureStorageProvider takes IConfiguration
        // and reads Storage:Azure:ConnectionString. AzureBlobStorageProvider
        // must NOT do that — it only knows about a pre-built BlobServiceClient
        // (which carries a TokenCredential, not a shared key) and a container
        // name. Any future refactor that re-introduces IConfiguration to its
        // constructor will trip this test.
        var ctorParams = typeof(AzureBlobStorageProvider)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters().Select(p => p.ParameterType))
            .ToArray();

        Assert.DoesNotContain(typeof(Microsoft.Extensions.Configuration.IConfiguration), ctorParams);
    }

    [Fact]
    public async Task CachingUserDelegationKeyProvider_FetchesOnce_ThenReturnsCached()
    {
        // Arrange: a fetcher that increments per call, so we can assert
        // it's only invoked once across many GetAsync() calls.
        var fetchCount = 0;
        CachingUserDelegationKeyProvider.UserDelegationKeyFetcher fetcher =
            (_, _, _) =>
            {
                Interlocked.Increment(ref fetchCount);
                return Task.FromResult(FakeUserDelegationKey());
            };

        using var cache = new CachingUserDelegationKeyProvider(
            fetcher,
            NullLogger<CachingUserDelegationKeyProvider>.Instance);

        // Act: hit the cache 10 times from a single thread
        for (var i = 0; i < 10; i++)
        {
            var udk = await cache.GetAsync();
            Assert.NotNull(udk);
        }

        // Assert
        Assert.Equal(1, fetchCount);
    }

    [Fact]
    public async Task CachingUserDelegationKeyProvider_IsThreadSafe_FetchesOnceUnderRace()
    {
        // Arrange: 50 concurrent first-time callers must collapse into exactly
        // one network fetch (semaphore-guarded double-check).
        var fetchCount = 0;
        CachingUserDelegationKeyProvider.UserDelegationKeyFetcher fetcher =
            async (_, _, ct) =>
            {
                Interlocked.Increment(ref fetchCount);
                await Task.Delay(20, ct); // simulate Azure AD latency
                return FakeUserDelegationKey();
            };

        using var cache = new CachingUserDelegationKeyProvider(
            fetcher,
            NullLogger<CachingUserDelegationKeyProvider>.Instance);

        // Act
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => cache.GetAsync())
            .ToArray();
        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(1, fetchCount);
    }
}

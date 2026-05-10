using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace PhotoGallery.Services.Storage;

/// <summary>
/// Default <see cref="IUserDelegationKeyProvider"/> backed by
/// <see cref="BlobServiceClient.GetUserDelegationKeyAsync"/>.
///
/// Behavior:
/// <list type="bullet">
///   <item>Fetches a fresh user-delegation key with a 7-day validity window.</item>
///   <item>Caches the key in-memory; refreshes ~6 days in (one-day safety
///         margin against the 7-day Azure cap so we never serve an
///         expired-mid-flight key).</item>
///   <item>Refresh is protected by a SemaphoreSlim — a thundering herd at
///         startup or refresh time triggers exactly one network call.</item>
/// </list>
///
/// Pure abstraction seam (<see cref="UserDelegationKeyFetcher"/>) is exposed
/// for unit tests so they don't have to mock <see cref="BlobServiceClient"/>.
/// </summary>
public sealed class CachingUserDelegationKeyProvider : IUserDelegationKeyProvider, IDisposable
{
    /// <summary>
    /// Test seam: fetch a UserDelegationKey for the supplied validity window.
    /// Production wires this to <see cref="BlobServiceClient.GetUserDelegationKeyAsync"/>.
    /// </summary>
    internal delegate Task<UserDelegationKey> UserDelegationKeyFetcher(
        DateTimeOffset startsOn,
        DateTimeOffset expiresOn,
        CancellationToken cancellationToken);

    private static readonly TimeSpan KeyValidity = TimeSpan.FromDays(7);
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(6);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private readonly UserDelegationKeyFetcher _fetcher;
    private readonly ILogger<CachingUserDelegationKeyProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private UserDelegationKey? _cachedKey;
    private DateTimeOffset _refreshAt = DateTimeOffset.MinValue;

    public CachingUserDelegationKeyProvider(
        BlobServiceClient blobServiceClient,
        ILogger<CachingUserDelegationKeyProvider> logger)
        : this(
            async (start, end, ct) =>
                (await blobServiceClient.GetUserDelegationKeyAsync(start, end, ct)).Value,
            logger)
    {
    }

    internal CachingUserDelegationKeyProvider(
        UserDelegationKeyFetcher fetcher,
        ILogger<CachingUserDelegationKeyProvider> logger)
    {
        _fetcher = fetcher;
        _logger = logger;
    }

    public async Task<UserDelegationKey> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedKey is not null && DateTimeOffset.UtcNow < _refreshAt)
        {
            return _cachedKey;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedKey is not null && DateTimeOffset.UtcNow < _refreshAt)
            {
                return _cachedKey;
            }

            var startsOn = DateTimeOffset.UtcNow.Subtract(ClockSkew);
            var expiresOn = DateTimeOffset.UtcNow.Add(KeyValidity);
            _logger.LogInformation(
                "Fetching new Azure Blob user-delegation key (validity {StartsOn} → {ExpiresOn}).",
                startsOn,
                expiresOn);

            _cachedKey = await _fetcher(startsOn, expiresOn, cancellationToken);
            _refreshAt = DateTimeOffset.UtcNow.Add(RefreshAfter);
            return _cachedKey;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

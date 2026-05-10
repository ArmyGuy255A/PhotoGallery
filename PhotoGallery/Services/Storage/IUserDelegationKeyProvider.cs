using Azure.Storage.Blobs.Models;

namespace PhotoGallery.Services.Storage;

/// <summary>
/// Process-wide cache for an Azure Blob user-delegation key.
///
/// User-delegation keys are issued by Azure AD against the BlobServiceClient's
/// TokenCredential (DefaultAzureCredential in our case) and are valid for up to
/// 7 days. They are used to sign user-delegation SAS URLs — the only SAS shape
/// available when the storage account has <c>shared_access_key_enabled = false</c>
/// (which is the Terraform default for the PhotoGallery-dev Storage Account).
///
/// Cached because fetching one is an Azure AD round-trip; refreshed before the
/// 7-day cap (the production impl refreshes every ~6 days).
/// </summary>
public interface IUserDelegationKeyProvider
{
    /// <summary>
    /// Returns a cached user-delegation key valid for SAS generation now and
    /// for the near future. Implementations refresh transparently as the key
    /// approaches expiry.
    /// </summary>
    Task<UserDelegationKey> GetAsync(CancellationToken cancellationToken = default);
}

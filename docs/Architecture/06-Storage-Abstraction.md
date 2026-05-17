# 06 — Storage Abstraction

How the codebase swaps MinIO for Azure Blob without any consumer knowing. This is the canonical example of provider abstraction in this codebase. Use it as the template when you introduce a new cross-cutting infrastructure dependency (queues, email, secrets).

## The seam

`PhotoGallery/Services/Storage/IStorageProvider.cs`. Every read, write, list, delete, and pre-signed URL request goes through this interface. No consumer ever touches `Amazon.S3.IAmazonS3` or `Azure.Storage.Blobs.BlobServiceClient` directly.

The interface is intentionally narrow. It covers the operations the application needs, not the union of every operation the providers can perform.

```csharp
public interface IStorageProvider
{
    Task<string>             UploadAsync(string key, Stream content, string contentType);
    Task<Stream>             DownloadAsync(string key);
    Task<bool>               DeleteAsync(string key);
    Task<string?>            GetUrlAsync(string key, int expirationMinutes = 60);
    Task<bool>               ExistsAsync(string key);
    Task<IEnumerable<string>> ListAsync(string prefix);
    Task<string>             GenerateWriteSasUrlAsync(string key, TimeSpan ttl);
    Task<IEnumerable<string>> ListSubPrefixesAsync(string prefix);
    Task<IEnumerable<BlobInfo>> ListWithMetadataAsync(string prefix);
    Task<int>                DeleteManyAsync(IEnumerable<string> keys);
}

public sealed record BlobInfo(string Key, long Size, DateTimeOffset LastModified);
```

## Implementations

| Provider                       | Where used    | Authentication                                                        |
| ------------------------------ | ------------- | --------------------------------------------------------------------- |
| `MinioStorageProvider`         | Local dev     | Access key + secret. Configured in `appsettings.Development.json`.    |
| `AzureBlobStorageProvider`     | Trial, Prod   | `DefaultAzureCredential` + user-delegation SAS. No shared keys.       |
| `AzureStorageProvider`         | Legacy        | Connection string. Kept for backward compatibility.                   |

`AzureBlobStorageProvider` is the one to use for any new Azure deployment. It works with Storage Accounts that have `shared_access_key_enabled = false` (the prod Terraform setting). The legacy `AzureStorageProvider` only works with connection-string auth and will fail on a hardened account.

## Selection

`PhotoGallery/Services/Storage/StorageProviderFactory.cs`. Reads `Storage:Provider` (or the legacy `Storage:Type`), constructs and returns the right implementation. `Program.cs` calls the factory once and registers the result as a singleton.

```
Storage:Provider value     Implementation                Notes
─────────────────────────  ───────────────────────────   ──────────────────────────────
Minio                       MinioStorageProvider          local dev default
AzureBlob                   AzureBlobStorageProvider      trial + prod preferred
Azure                       AzureStorageProvider          legacy, connection-string
```

The factory is the only place that knows about concrete types. Every consumer takes `IStorageProvider` in its constructor.

## Key naming convention

All keys follow `photogallery/{albumId}/{photoId}/{quality}.jpg`. This is enforced by callers, not by the provider. The `OrphanedBlobReaperService` walks this hierarchy with `ListSubPrefixesAsync`, the `StorageConsistencyService` reads/writes by full key. If a new use case needs a different prefix, treat that as a separate decision and document it in an ADR.

## How to swap providers

In production, set `Storage:Provider = AzureBlob` and provide `Storage:AzureBlob:AccountUrl`. That is the whole change. No code change, no migration.

```
appsettings.Development.json:  Storage:Provider = Minio    (defaults to localhost:9000)
appsettings.Trial.json:        Storage:Provider = AzureBlob
appsettings.Production.json:   Storage:Provider = AzureBlob
```

For local dev a developer can override to AzureBlob by setting `Storage:Provider = AzureBlob` in `appsettings.Trial.Local.json` (gitignored) and `az login`-ing.

## Pre-signed URL semantics

Each provider implements pre-signed URLs with the auth model the underlying backend supports:

| Provider                       | Read URL                            | Write URL                                              |
| ------------------------------ | ----------------------------------- | ------------------------------------------------------ |
| `MinioStorageProvider`         | S3 pre-signed GET                   | S3 pre-signed PUT, single-blob scope                   |
| `AzureBlobStorageProvider`     | User-delegation SAS, read-only      | User-delegation SAS, write+create, single-blob scope   |

The user-delegation SAS path requires a cached delegation key. `CachingUserDelegationKeyProvider` caches it and refreshes near expiry. The key is the Microsoft Entra credential indirected to a SAS, so we never store account keys anywhere.

## Pitfalls and rules

These have bitten this codebase. Worth knowing.

| Rule                                                                        | Why                                                                                       | Where                                                       |
| --------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- | ----------------------------------------------------------- |
| `GetPreSignedUrlRequest.Protocol` must be set explicitly on Minio           | AWS SDK ignores `AmazonS3Config.ServiceURL` scheme and defaults to HTTPS.                 | `MinioStorageProvider.GetUrlAsync`                          |
| `S3Objects` can be null when a prefix has no matches                         | AWS SDK reuses the wire null. Foreach + Select throws `ArgumentNullException`.            | `MinioStorageProvider.ListAsync` and `ListWithMetadataAsync`|
| Never use the legacy `AzureStorageProvider` on a hardened Storage Account    | It tries shared-key auth, which is disabled on prod accounts.                              | `StorageProviderFactory.CreateAzureProvider`                |
| User-delegation SAS expiry must be ≤ the delegation key expiry              | Otherwise the SAS fails with 403 the moment the key rotates.                              | `AzureBlobStorageProvider.GetUrlAsync`                      |
| Public-visitor URLs use a short TTL (`BlobStorage:PublicUrlTtlMinutes`)      | Avoids letting a URL outlive the share decision.                                           | `PhotoVersionUrlService`                                    |
| Cached URL TTL must be < SAS TTL minus a safety margin                       | Otherwise the cache hands out URLs that expire mid-request.                                | `PhotoVersionUrlService` (3-minute floor today)             |

## The pattern (for new providers)

When you introduce a new infrastructure abstraction (e.g. `IQueue<T>`, `IEmailProvider`), copy the storage shape:

1. Narrow interface in `Services/<Domain>/I{Thing}Provider.cs`. Methods follow consumer needs, not the union.
2. One implementation per backend in the same folder. Concrete types never leak above this folder.
3. Factory in the same folder. Reads `<Domain>:Provider` from config, returns the right implementation.
4. `Program.cs` calls the factory once, registers the result as a singleton.
5. Consumers inject the interface.
6. Document the swap mechanics in this file pattern.

The agent skill `provider-abstraction-pattern` has the broader rationale. Specific channels have recipes: `queue-provider-abstraction`, `blob-provider-abstraction`, `relational-provider-abstraction`.

## Where to read next

* What gets stored: [01-Processing-Pipeline.md](01-Processing-Pipeline.md) (the seven blobs).
* The reaper: see "Reconciliation, not coordination" in [00-Overview.md](00-Overview.md).
* The factory pattern in general: `factory-pattern-recipe` skill.

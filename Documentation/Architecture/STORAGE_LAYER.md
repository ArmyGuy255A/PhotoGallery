# Storage Layer

**📍 Navigation**
- 🏠 [Documentation Index](../INDEX.md)
- 🏗️ [Design Decisions](./DESIGN_DECISIONS.md) - All approved design decisions (D002: Storage Provider Abstraction)
- 🏗️ [System Architecture](./SYSTEM_ARCHITECTURE.md) - Component overview
- 💾 [Database Schema](./DATABASE_SCHEMA.md) - Entity relationships
- 🔌 [API Design](./API_DESIGN.md) - REST endpoint patterns
- 🔐 [Authentication](./AUTHENTICATION.md) - OAuth and JWT patterns
- 📚 [All Guides](../Guides/) - TDD, Docker, CI/CD, Startup

---

# Storage Layer Architecture

## Overview

PhotoGallery uses an abstracted storage layer that supports multiple backends (MinIO for development, Azure for production) through a consistent interface.

**See [DESIGN_DECISIONS.md](./DESIGN_DECISIONS.md) - D002: Storage Provider Abstraction Layer**

## Storage Path Structure

```
photogallery/
├── {album_guid}/                       # Album namespace
│   └── {photo_guid}/                   # Photo namespace
│       ├── original.jpg                # Uploaded file (full resolution, served to paid carts)
│       ├── high.jpg                    # 50% compression
│       ├── medium.jpg                  # 75% compression
│       ├── medium-watermarked.jpg      # 75% compression + tiled watermark (paid-photo guest preview)
│       ├── thumbnail-watermarked.jpg   # Thumbnail + tiled watermark (paid-photo guest preview, PR #48)
│       ├── low.jpg                     # 85% compression
│       └── raw.jpg                     # 100% quality
```

> **Original quality** (`original.jpg`) was added to the cart-quality choices in PR #50. Cart manifests requesting Original return a presigned URL pointing at this object directly — no resize, no re-encode.
>
> **Watermarked thumbnails** (`thumbnail-watermarked.jpg`) were introduced in PR #48 alongside a one-shot backfill that produced the variant for every existing photo. See [docs/decisions/D009-watermark-pipeline.md](../../docs/decisions/D009-watermark-pipeline.md).

**See [DESIGN_DECISIONS.md](./DESIGN_DECISIONS.md) - D005: Storage Path Structure Standard**

## IStorageProvider Interface

```csharp
public interface IStorageProvider
{
    /// Upload file to storage
    Task<string> UploadAsync(string key, Stream stream, string contentType);
    
    /// Download file from storage
    Task<Stream> DownloadAsync(string key);
    
    /// Delete file from storage
    Task DeleteAsync(string key);
    
    /// Get presigned URL for temporary public access
    Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry);
    
    /// Check if file exists
    Task<bool> ExistsAsync(string key);
    
    /// List all files with prefix
    Task<IEnumerable<string>> ListAsync(string prefix);
}
```

## Implementations

### MinioStorageProvider (Development)

```csharp
public class MinioStorageProvider : IStorageProvider
{
    // Uses AWSSDK.S3 with MinIO S3-compatible API
    // Configuration: appsettings.Development.json
    private readonly IAmazonS3 s3Client;
    
    public async Task<string> UploadAsync(string key, Stream stream, string contentType)
    {
        // Uploads to MinIO bucket
    }
}
```

**Configuration**:
```json
{
  "Storage": {
    "Provider": "minio",
    "Minio": {
      "Endpoint": "http://localhost:9000",
      "AccessKey": "minioadmin",
      "SecretKey": "minioadmin",
      "BucketName": "photogallery",
      "UseSSL": false
    }
  }
}
```

### AzureStorageProvider (Production)

```csharp
public class AzureStorageProvider : IStorageProvider
{
    // Uses Azure.Storage.Blobs SDK
    // Configuration: appsettings.Production.json
    private readonly BlobContainerClient containerClient;
    
    public async Task<string> UploadAsync(string key, Stream stream, string contentType)
    {
        // Uploads to Azure Blob Storage
    }
}
```

**Configuration**:
```json
{
  "Storage": {
    "Provider": "azure",
    "Azure": {
      "ConnectionString": "DefaultEndpointsProtocol=https;...",
      "ContainerName": "photogallery"
    }
  }
}
```

## StorageProviderFactory

Selects provider based on environment:

```csharp
public class StorageProviderFactory
{
    public static IStorageProvider Create(string provider)
    {
        return provider switch
        {
            "minio" => new MinioStorageProvider(...),
            "azure" => new AzureStorageProvider(...),
            _ => throw new InvalidOperationException($"Unknown provider: {provider}")
        };
    }
}
```

## Usage Example

```csharp
[ApiController]
[Route("api/photos")]
public class PhotosController
{
    private readonly IStorageProvider storageProvider;
    
    public PhotosController(IStorageProvider storageProvider)
    {
        this.storageProvider = storageProvider;
    }
    
    [HttpPost("albums/{albumId}")]
    [Authorize]
    public async Task<IActionResult> UploadPhoto(
        Guid albumId,
        IFormFile file)
    {
        var photo = Photo.Create(albumId, file);
        
        // Upload to storage (abstracted provider)
        var storageKey = $"photogallery/{albumId}/{photo.Id}/original.jpg";
        await storageProvider.UploadAsync(
            storageKey,
            file.OpenReadStream(),
            file.ContentType);
        
        photo.StorageKey = storageKey;
        await photoRepository.AddAsync(photo);
        
        return Ok(photo);
    }
}
```

## Docker Setup

### MinIO Container

```yaml
services:
  minio:
    image: minio/minio:latest
    ports:
      - "9000:9000"     # API
      - "9001:9001"     # Console
    environment:
      MINIO_ROOT_USER: minioadmin
      MINIO_ROOT_PASSWORD: minioadmin
    volumes:
      - minio_data:/data
    command: minio server /data --console-address ":9001"
```

**Access**:
- API: http://localhost:9000
- Console: http://localhost:9001 (minioadmin / minioadmin)

## CORS for SPA Streaming Downloads

The SPA fetches photos **directly** from blob storage in two situations:

1. **Inline `<img>` rendering** — uses presigned URLs in the `src` attribute. Browsers do not enforce CORS for image elements, so MinIO/Azure can serve these without any CORS config.
2. **Cart download via [`client-zip`](https://github.com/Touffy/client-zip)** (see [DESIGN_DECISIONS.md — D010](./DESIGN_DECISIONS.md#d010-cart-download--client-side-zip-via-manifest-endpoint)) — uses `fetch(presignedUrl) → ReadableStream` to stream each photo into a client-side ZIP. **This path is a CORS request** because the response stream is consumed by JavaScript, not just rendered.

Without CORS configured on the storage backend, the cart download fails with `TypeError: NetworkError when attempting to fetch resource` and a console message about a missing `Access-Control-Allow-Origin` header.

### Why the SPA fetches storage directly (and not via the backend)

Routing photo bytes through the backend would mean:
- Every byte travels storage → backend → browser, doubling bandwidth.
- The backend becomes a relay with no value-add and a memory ceiling at large cart sizes.
- Sync-IO complications around `ZipArchive.Dispose` (the original failure that motivated D010).

Direct-from-storage `fetch` lets MinIO / Azure Blob serve at their own throughput, and `client-zip` assembles the archive locally with constant backend memory.

### MinIO (development)

The repo ships `scripts/setup-minio-cors.ps1` which bootstraps the MinIO bucket policy via the `mc` admin client. Run it once after `docker compose up`:

```powershell
.\scripts\setup-minio-cors.ps1
```

If the script is unavailable, the equivalent `mc` commands are:

```bash
# Configure the mc alias for the local MinIO container
mc alias set local http://localhost:9000 minioadmin minioadmin

# Allow the SPA dev origin (or '*' for fully open dev)
mc admin config set local cors_allow_origin="http://localhost:4300"
mc admin service restart local
```

For older MinIO releases that do not expose the `cors_allow_origin` admin key, set the equivalent environment variables on the MinIO container:

```yaml
# docker-compose.yml (excerpt)
services:
  minio:
    environment:
      MINIO_API_CORS_ALLOW_ORIGIN: "http://localhost:4300"
```

### Azure Blob Storage (production)

Azure Storage exposes a per-account CORS rule list. Add the SPA origin via the Azure CLI:

```bash
az storage cors add \
  --services b \
  --methods GET HEAD \
  --origins "https://photogallery.example.com" \
  --allowed-headers "*" \
  --exposed-headers "*" \
  --max-age 3600 \
  --account-name <storage-account>
```

**Notes**:
- `--origins` should be the exact SPA origin(s); avoid `*` in production so credentials and signed URLs cannot be exfiltrated by arbitrary origins.
- `--methods GET HEAD` is sufficient — the SPA only reads. Uploads still go through the backend.
- The change propagates within ~30 seconds; verify with `az storage cors list --services b --account-name <account>`.

### Smoke test

After bootstrap, from a SPA-origin browser tab:

```js
const r = await fetch('<presigned-url>', { method: 'GET' });
console.log(r.headers.get('access-control-allow-origin'));
// Expect: 'http://localhost:4300' (or '*' in dev)
```

## Migration Between Providers

To migrate from MinIO to Azure:

1. **Export data from MinIO**:
```bash
aws s3 cp s3://photogallery . --recursive --endpoint-url http://localhost:9000
```

2. **Import to Azure**:
```bash
az storage blob upload-batch -d photogallery -s . --account-name myaccount
```

3. **Update configuration**:
```json
{
  "Storage": {
    "Provider": "azure"
  }
}
```

4. **Restart backend**: No code changes needed!

## Testing Storage Providers

```csharp
[Fact]
public async Task UploadAsync_Should_Store_File()
{
    // Arrange
    var mockStorage = new Mock<IStorageProvider>();
    mockStorage
        .Setup(x => x.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
        .ReturnsAsync("key");
    
    var service = new PhotoUploadService(mockStorage.Object);
    var file = new MemoryStream(new byte[] { 1, 2, 3 });
    
    // Act
    await service.UploadAsync(file, "image/jpeg");
    
    // Assert
    mockStorage.Verify(x => x.UploadAsync(
        It.IsAny<string>(),
        It.IsAny<Stream>(),
        "image/jpeg"),
        Times.Once);
}
```

---

## Adding a New Storage Provider

1. **Implement IStorageProvider**:
```csharp
public class GoogleCloudStorageProvider : IStorageProvider
{
    // Implement all interface methods
}
```

2. **Register in factory**:
```csharp
return provider switch
{
    "minio" => new MinioStorageProvider(...),
    "azure" => new AzureStorageProvider(...),
    "google" => new GoogleCloudStorageProvider(...), // NEW!
    _ => throw new InvalidOperationException()
};
```

3. **Add configuration**:
```json
{
  "Storage": {
    "Provider": "google",
    "Google": { "ProjectId": "...", "BucketName": "..." }
  }
}
```

4. **No code changes needed elsewhere!** (Benefits of abstraction)

---

## Related Documentation

- 🏗️ [Design Decisions](./DESIGN_DECISIONS.md) - D002 & D005 explain storage design
- 🏗️ [System Architecture](./SYSTEM_ARCHITECTURE.md) - Storage component diagram
- 📚 [Guides](../Guides/) - Docker setup, CI/CD configuration

---

**Last Updated**: 2026-05-03  
**Current Providers**: MinIO (dev), Azure (prod)  
**Status**: Operational in Phase 12  
**Related Decisions**: [D002](./DESIGN_DECISIONS.md) • [D005](./DESIGN_DECISIONS.md)

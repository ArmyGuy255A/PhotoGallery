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
├── {album_guid}/           # Album namespace
│   └── {photo_guid}/       # Photo namespace
│       ├── original.jpg    # Uploaded file (not processed)
│       ├── high.jpg        # 50% compression
│       ├── medium.jpg      # 75% compression
│       ├── low.jpg         # 85% compression
│       └── raw.jpg         # 100% quality
```

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

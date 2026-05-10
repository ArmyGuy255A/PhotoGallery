using Xunit;
using Moq;
using PhotoGallery.Models;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Services;
using PhotoGallery.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PhotoGallery.Tests;

public class PhotoVersionUrlServiceTests
{
    private readonly Mock<IStorageProvider> _mockStorageProvider;
    private readonly Mock<IPhotoVersionUrlRepository> _mockUrlRepository;
    private readonly Mock<IPhotoRepository> _mockPhotoRepository;
    private readonly IConfiguration _configuration;
    private readonly Mock<ILogger<PhotoVersionUrlService>> _mockLogger;
    private readonly PhotoVersionUrlService _service;

    public PhotoVersionUrlServiceTests()
    {
        _mockStorageProvider = new Mock<IStorageProvider>();
        _mockUrlRepository = new Mock<IPhotoVersionUrlRepository>();
        _mockPhotoRepository = new Mock<IPhotoRepository>();
        _mockLogger = new Mock<ILogger<PhotoVersionUrlService>>();

        // Create a real configuration object for testing.
        // BlobStorage:VerifyCachedUrls defaults to true here to match the production/dev default
        // introduced by D008 (Cached Pre-Signed URL Storage Verification).
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"BlobStorage:PreSignedUrlTTLDays", "7"},
            {"BlobStorage:PreSignedUrlRefreshWindowDays", "5"},
            {"BlobStorage:CachedQualities:0", "Thumbnail"},
            {"BlobStorage:CachedQualities:1", "Medium"},
            {"BlobStorage:VerifyCachedUrls", "true"},
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _service = new PhotoVersionUrlService(
            _mockStorageProvider.Object,
            _mockUrlRepository.Object,
            _mockPhotoRepository.Object,
            _configuration,
            _mockLogger.Object);
    }

    /// <summary>
    /// Construct a service variant with a custom value for BlobStorage:VerifyCachedUrls.
    /// Used by the D008 "verification disabled" test to exercise the legacy/no-verify path.
    /// </summary>
    private PhotoVersionUrlService BuildServiceWithVerifyCachedUrls(bool verify)
    {
        var settings = new Dictionary<string, string?>
        {
            {"BlobStorage:PreSignedUrlTTLDays", "7"},
            {"BlobStorage:PreSignedUrlRefreshWindowDays", "5"},
            {"BlobStorage:CachedQualities:0", "Thumbnail"},
            {"BlobStorage:CachedQualities:1", "Medium"},
            {"BlobStorage:VerifyCachedUrls", verify ? "true" : "false"},
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return new PhotoVersionUrlService(
            _mockStorageProvider.Object,
            _mockUrlRepository.Object,
            _mockPhotoRepository.Object,
            configuration,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GetPhotoVersionUrlAsync_Should_Return_Cached_Url_If_Valid()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var cachedUrl = new PhotoVersionUrl
        {
            Id = Guid.NewGuid(),
            PhotoId = photoId,
            Quality = QualityType.Thumbnail,
            PresignedUrl = "http://minio/cached-url",
            ExpiresAt = now.AddDays(5),  // Still valid
            GeneratedAt = now,
            IsActive = true
        };

        _mockUrlRepository
            .Setup(x => x.GetByPhotoAndQualityAsync(photoId, QualityType.Thumbnail))
            .ReturnsAsync(cachedUrl);

        // D008: GetPhotoVersionUrlAsync now verifies the cached URL still backs a real
        // storage object before returning it. We must mock the photo lookup (used to
        // build the storage key) and an ExistsAsync = true response.
        _mockPhotoRepository
            .Setup(x => x.GetByIdAsync(photoId))
            .ReturnsAsync(new Photo { Id = photoId, AlbumId = albumId, FileName = "test.jpg" });
        _mockStorageProvider
            .Setup(x => x.ExistsAsync($"photogallery/{albumId}/{photoId}/thumbnail.jpg"))
            .ReturnsAsync(true);

        // Act
        var result = await _service.GetPhotoVersionUrlAsync(photoId, QualityType.Thumbnail);

        // Assert
        Assert.Equal("http://minio/cached-url", result);
        _mockUrlRepository.Verify(x => x.GetByPhotoAndQualityAsync(photoId, QualityType.Thumbnail), Times.Once);
        _mockStorageProvider.Verify(x => x.GetUrlAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetPhotoVersionUrlAsync_Should_Regenerate_Url_If_Expired()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        
        var expiredUrl = new PhotoVersionUrl
        {
            Id = Guid.NewGuid(),
            PhotoId = photoId,
            Quality = QualityType.Thumbnail,
            PresignedUrl = "http://minio/old-url",
            ExpiresAt = now.AddDays(-1),  // Expired
            GeneratedAt = now.AddDays(-7),
            IsActive = true
        };

        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "test.jpg" };

        _mockUrlRepository
            .Setup(x => x.GetByPhotoAndQualityAsync(photoId, QualityType.Thumbnail))
            .ReturnsAsync(expiredUrl);

        // D008: CachePhotoVersionUrlAsync now uses the include-inactive variant so it can
        // overwrite the existing row in place rather than insert a sibling (which would
        // violate the unique (PhotoId, Quality) index).
        _mockUrlRepository
            .Setup(x => x.GetByPhotoAndQualityIncludingInactiveAsync(photoId, QualityType.Thumbnail))
            .ReturnsAsync(expiredUrl);

        _mockPhotoRepository
            .Setup(x => x.GetByIdAsync(photoId))
            .ReturnsAsync(photo);

        _mockStorageProvider
            .Setup(x => x.ExistsAsync($"photogallery/{albumId}/{photoId}/thumbnail.jpg"))
            .ReturnsAsync(true);

        _mockStorageProvider
            .Setup(x => x.GetUrlAsync($"photogallery/{albumId}/{photoId}/thumbnail.jpg", It.IsAny<int>()))
            .ReturnsAsync("http://minio/new-url");

        _mockUrlRepository
            .Setup(x => x.UpdateAsync(It.IsAny<PhotoVersionUrl>()))
            .Returns(Task.CompletedTask);

        _mockUrlRepository
            .Setup(x => x.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.GetPhotoVersionUrlAsync(photoId, QualityType.Thumbnail);

        // Assert
        Assert.Equal("http://minio/new-url", result);
        _mockStorageProvider.Verify(x => x.GetUrlAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Once);
        _mockUrlRepository.Verify(x => x.UpdateAsync(It.IsAny<PhotoVersionUrl>()), Times.Once);
    }

    [Fact]
    public async Task GetPhotoVersionUrlAsync_Should_Generate_Url_If_Not_Cached()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "test.jpg" };

        _mockUrlRepository
            .Setup(x => x.GetByPhotoAndQualityAsync(photoId, QualityType.Thumbnail))
            .ReturnsAsync((PhotoVersionUrl?)null);

        _mockPhotoRepository
            .Setup(x => x.GetByIdAsync(photoId))
            .ReturnsAsync(photo);

        _mockStorageProvider
            .Setup(x => x.ExistsAsync($"photogallery/{albumId}/{photoId}/thumbnail.jpg"))
            .ReturnsAsync(true);

        _mockStorageProvider
            .Setup(x => x.GetUrlAsync($"photogallery/{albumId}/{photoId}/thumbnail.jpg", It.IsAny<int>()))
            .ReturnsAsync("http://minio/generated-url");

        _mockUrlRepository
            .Setup(x => x.AddAsync(It.IsAny<PhotoVersionUrl>()))
            .Returns(Task.CompletedTask);

        _mockUrlRepository
            .Setup(x => x.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.GetPhotoVersionUrlAsync(photoId, QualityType.Thumbnail);

        // Assert
        Assert.Equal("http://minio/generated-url", result);
        _mockStorageProvider.Verify(x => x.GetUrlAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Once);
        _mockUrlRepository.Verify(x => x.AddAsync(It.IsAny<PhotoVersionUrl>()), Times.Once);
    }

    [Fact]
    public async Task GeneratePhotoVersionUrlsAsync_Should_Generate_All_Qualities()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "test.jpg" };

        _mockPhotoRepository
            .Setup(x => x.GetByIdAsync(photoId))
            .ReturnsAsync(photo);

        _mockStorageProvider
            .Setup(x => x.ExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        _mockStorageProvider
            .Setup(x => x.GetUrlAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync("http://minio/url");

        _mockUrlRepository
            .Setup(x => x.AddAsync(It.IsAny<PhotoVersionUrl>()))
            .Returns(Task.CompletedTask);

        _mockUrlRepository
            .Setup(x => x.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.GeneratePhotoVersionUrlsAsync(photoId);

        // Assert
        Assert.Equal(5, result.Count);  // All 5 qualities (Thumbnail, Low, Medium, High, Original)
        Assert.Contains(QualityType.Thumbnail, result.Keys);
        Assert.Contains(QualityType.Low, result.Keys);
        Assert.Contains(QualityType.Medium, result.Keys);
        Assert.Contains(QualityType.High, result.Keys);
        Assert.Contains(QualityType.Original, result.Keys);

        // Verify storage was called for each quality
        _mockStorageProvider.Verify(x => x.ExistsAsync(It.IsAny<string>()), Times.Exactly(5));
        _mockStorageProvider.Verify(x => x.GetUrlAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(5));

        // Original key resolves to the upload-pipeline path
        _mockStorageProvider.Verify(
            x => x.ExistsAsync($"photogallery/{albumId}/{photoId}/original.jpg"),
            Times.Once);

        // Only Thumbnail and Medium should be cached
        _mockUrlRepository.Verify(x => x.AddAsync(It.IsAny<PhotoVersionUrl>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GeneratePhotoVersionUrlsAsync_Should_Return_Empty_If_Photo_Not_Found()
    {
        // Arrange
        var photoId = Guid.NewGuid();

        _mockPhotoRepository
            .Setup(x => x.GetByIdAsync(photoId))
            .ReturnsAsync((Photo?)null);

        // Act
        var result = await _service.GeneratePhotoVersionUrlsAsync(photoId);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);  // No URLs generated
        _mockStorageProvider.Verify(x => x.ExistsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GeneratePhotoVersionUrlsAsync_Should_Handle_Missing_Files()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "test.jpg" };

        _mockPhotoRepository
            .Setup(x => x.GetByIdAsync(photoId))
            .ReturnsAsync(photo);

        // Only Thumbnail exists, others don't
        _mockStorageProvider
            .Setup(x => x.ExistsAsync($"photogallery/{albumId}/{photoId}/thumbnail.jpg"))
            .ReturnsAsync(true);

        _mockStorageProvider
            .Setup(x => x.ExistsAsync(It.IsNotIn($"photogallery/{albumId}/{photoId}/thumbnail.jpg")))
            .ReturnsAsync(false);

        _mockStorageProvider
            .Setup(x => x.GetUrlAsync($"photogallery/{albumId}/{photoId}/thumbnail.jpg", It.IsAny<int>()))
            .ReturnsAsync("http://minio/thumbnail-url");

        _mockUrlRepository
            .Setup(x => x.AddAsync(It.IsAny<PhotoVersionUrl>()))
            .Returns(Task.CompletedTask);

        _mockUrlRepository
            .Setup(x => x.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.GeneratePhotoVersionUrlsAsync(photoId);

        // Assert
        Assert.Equal(5, result.Count);
        Assert.NotNull(result[QualityType.Thumbnail]);
        Assert.Null(result[QualityType.Low]);
        Assert.Null(result[QualityType.Medium]);
        Assert.Null(result[QualityType.High]);
        Assert.Null(result[QualityType.Original]);
    }

    [Fact]
    public async Task GetPhotoVersionUrlAsync_Should_Handle_Storage_Provider_Error()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "test.jpg" };

        _mockUrlRepository
            .Setup(x => x.GetByPhotoAndQualityAsync(photoId, QualityType.Thumbnail))
            .ReturnsAsync((PhotoVersionUrl?)null);

        _mockPhotoRepository
            .Setup(x => x.GetByIdAsync(photoId))
            .ReturnsAsync(photo);

        _mockStorageProvider
            .Setup(x => x.ExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        _mockStorageProvider
            .Setup(x => x.GetUrlAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new Exception("Storage provider error"));

        // Act
        var result = await _service.GetPhotoVersionUrlAsync(photoId, QualityType.Thumbnail);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPhotoVersionUrlAsync_Should_Not_Cache_Low_Quality()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "test.jpg" };

        _mockPhotoRepository
            .Setup(x => x.GetByIdAsync(photoId))
            .ReturnsAsync(photo);

        _mockStorageProvider
            .Setup(x => x.ExistsAsync($"photogallery/{albumId}/{photoId}/low.jpg"))
            .ReturnsAsync(true);

        _mockStorageProvider
            .Setup(x => x.GetUrlAsync($"photogallery/{albumId}/{photoId}/low.jpg", It.IsAny<int>()))
            .ReturnsAsync("http://minio/low-url");

        // Act
        var result = await _service.GetPhotoVersionUrlAsync(photoId, QualityType.Low);

        // Assert
        Assert.Equal("http://minio/low-url", result);

        // Low quality should NOT be added to repository (no caching)
        _mockUrlRepository.Verify(x => x.AddAsync(It.IsAny<PhotoVersionUrl>()), Times.Never);
        _mockUrlRepository.Verify(x => x.UpdateAsync(It.IsAny<PhotoVersionUrl>()), Times.Never);
    }

    // -----------------------------------------------------------------------------------
    // D008: Cached Pre-Signed URL Storage Verification
    // -----------------------------------------------------------------------------------
    // The cached-return path in PhotoVersionUrlService.GetPhotoVersionUrlAsync historically
    // returned the cached URL without verifying that the underlying storage object still
    // exists. When the object is gone (manual cleanup, drift between DB and storage), the
    // pre-signed URL is "valid" by signature but yields 404 — producing the broken-image
    // icon in the album cards.
    //
    // D008 says:
    //   1. When VerifyCachedUrls is true, call ExistsAsync(storageKey) before returning
    //      a cached URL.
    //   2. If the object is missing, regenerate by overwriting the existing row in place
    //      (must NOT insert a new row — there is a unique index on (PhotoId, Quality) per
    //      PhotoVersionUrlConfiguration.cs:35).
    //   3. When VerifyCachedUrls is false, behave exactly like before (no ExistsAsync call).
    //
    // The four tests below cover the full contract.
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task GetPhotoVersionUrlAsync_WhenCachedUrlPointsToMissingFile_RegeneratesAndOverwritesRow()
    {
        // Arrange: a valid (not expired) cached URL exists, but the underlying storage
        // object is gone. The service must detect this, regenerate, and reuse the same
        // PhotoVersionUrl row (UpdateAsync, not AddAsync).
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var storageKey = $"photogallery/{albumId}/{photoId}/thumbnail.jpg";

        var cachedUrl = new PhotoVersionUrl
        {
            Id = Guid.NewGuid(),
            PhotoId = photoId,
            Quality = QualityType.Thumbnail,
            PresignedUrl = "http://minio/STALE-url",
            ExpiresAt = now.AddDays(5),
            GeneratedAt = now,
            IsActive = true
        };
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "test.jpg" };

        _mockUrlRepository
            .Setup(x => x.GetByPhotoAndQualityAsync(photoId, QualityType.Thumbnail))
            .ReturnsAsync(cachedUrl);
        _mockUrlRepository
            .Setup(x => x.GetByPhotoAndQualityIncludingInactiveAsync(photoId, QualityType.Thumbnail))
            .ReturnsAsync(cachedUrl);
        _mockPhotoRepository
            .Setup(x => x.GetByIdAsync(photoId))
            .ReturnsAsync(photo);

        // Storage says the object is GONE on the verification call,
        // but PRESENT on the subsequent regeneration call (we mock the same key for
        // both because regeneration also calls ExistsAsync internally; the file came
        // back, e.g., a backfill ran in between, OR the regeneration uses the same
        // key). For this test we simulate: verification = false, regeneration = true.
        // We use a sequence so the first call returns false and the next returns true.
        _mockStorageProvider
            .SetupSequence(x => x.ExistsAsync(storageKey))
            .ReturnsAsync(false)   // D008 verification: object is missing
            .ReturnsAsync(true);   // GeneratePhotoVersionUrlAsync's internal check
        _mockStorageProvider
            .Setup(x => x.GetUrlAsync(storageKey, It.IsAny<int>()))
            .ReturnsAsync("http://minio/FRESH-url");

        // Act
        var result = await _service.GetPhotoVersionUrlAsync(photoId, QualityType.Thumbnail);

        // Assert: stale URL was NOT returned; fresh URL was generated.
        Assert.Equal("http://minio/FRESH-url", result);

        // The existing row must be UPDATED in place (not a new row added) — otherwise
        // the unique constraint on (PhotoId, Quality) would be violated.
        _mockUrlRepository.Verify(x => x.UpdateAsync(It.IsAny<PhotoVersionUrl>()), Times.AtLeastOnce);
        _mockUrlRepository.Verify(x => x.AddAsync(It.IsAny<PhotoVersionUrl>()), Times.Never);

        // ExistsAsync must have been called on the verification path.
        _mockStorageProvider.Verify(x => x.ExistsAsync(storageKey), Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetPhotoVersionUrlAsync_WhenCachedUrlPointsToMissingFileAndStorageStaysGone_ReturnsNull()
    {
        // Arrange: cached URL exists, storage says missing on BOTH the verification call
        // and the regeneration call. Result: null (caller renders placeholder).
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var storageKey = $"photogallery/{albumId}/{photoId}/thumbnail.jpg";

        var cachedUrl = new PhotoVersionUrl
        {
            Id = Guid.NewGuid(),
            PhotoId = photoId,
            Quality = QualityType.Thumbnail,
            PresignedUrl = "http://minio/STALE-url",
            ExpiresAt = now.AddDays(5),
            GeneratedAt = now,
            IsActive = true
        };
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "test.jpg" };

        _mockUrlRepository
            .Setup(x => x.GetByPhotoAndQualityAsync(photoId, QualityType.Thumbnail))
            .ReturnsAsync(cachedUrl);
        _mockUrlRepository
            .Setup(x => x.GetByPhotoAndQualityIncludingInactiveAsync(photoId, QualityType.Thumbnail))
            .ReturnsAsync(cachedUrl);
        _mockPhotoRepository
            .Setup(x => x.GetByIdAsync(photoId))
            .ReturnsAsync(photo);
        _mockStorageProvider
            .Setup(x => x.ExistsAsync(storageKey))
            .ReturnsAsync(false);   // gone, and stays gone

        // Act
        var result = await _service.GetPhotoVersionUrlAsync(photoId, QualityType.Thumbnail);

        // Assert: null — storage genuinely gone, caller should render placeholder.
        Assert.Null(result);

        // The stale URL must NOT have been returned.
        // We did NOT generate a new URL because the file is gone.
        _mockStorageProvider.Verify(x => x.GetUrlAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);

        // No AddAsync (would violate unique constraint).
        _mockUrlRepository.Verify(x => x.AddAsync(It.IsAny<PhotoVersionUrl>()), Times.Never);
    }

    [Fact]
    public async Task GetPhotoVersionUrlAsync_WhenVerifyCachedUrlsDisabled_DoesNotCallExistsAsyncOnCachedPath()
    {
        // Arrange: VerifyCachedUrls=false (legacy / opt-out behavior).
        // Cached URL exists and is valid; service must return it WITHOUT calling ExistsAsync.
        var photoId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var cachedUrl = new PhotoVersionUrl
        {
            Id = Guid.NewGuid(),
            PhotoId = photoId,
            Quality = QualityType.Thumbnail,
            PresignedUrl = "http://minio/cached-url",
            ExpiresAt = now.AddDays(5),
            GeneratedAt = now,
            IsActive = true
        };

        _mockUrlRepository
            .Setup(x => x.GetByPhotoAndQualityAsync(photoId, QualityType.Thumbnail))
            .ReturnsAsync(cachedUrl);

        var serviceNoVerify = BuildServiceWithVerifyCachedUrls(verify: false);

        // Act
        var result = await serviceNoVerify.GetPhotoVersionUrlAsync(photoId, QualityType.Thumbnail);

        // Assert: cached URL returned, ExistsAsync never called.
        Assert.Equal("http://minio/cached-url", result);
        _mockStorageProvider.Verify(x => x.ExistsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CachePhotoVersionUrlAsync_WhenInactiveRowExists_UpdatesExistingRowAndReactivates()
    {
        // Arrange: this exercises the upsert path inside CachePhotoVersionUrlAsync after
        // D008's repository change. Prior to D008, GetByPhotoAndQualityAsync filtered by
        // IsActive=true, so an inactive row was invisible to the cache-write path and a
        // duplicate AddAsync would throw on the unique (PhotoId, Quality) index.
        //
        // D008 adds GetByPhotoAndQualityIncludingInactiveAsync, which returns the inactive
        // row so the service can UPDATE it in place (overwriting PresignedUrl, IsActive=true,
        // ExpiresAt, GeneratedAt).
        //
        // We trigger the cache-write path by calling GetPhotoVersionUrlAsync with no active
        // cached row (GetByPhotoAndQualityAsync returns null), but an inactive row IS
        // present (GetByPhotoAndQualityIncludingInactiveAsync returns it). After regeneration,
        // the existing row must be Update()d, not Add()ed, and IsActive must be true.
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var storageKey = $"photogallery/{albumId}/{photoId}/thumbnail.jpg";

        var inactiveRow = new PhotoVersionUrl
        {
            Id = Guid.NewGuid(),
            PhotoId = photoId,
            Quality = QualityType.Thumbnail,
            PresignedUrl = "http://minio/old-and-invalidated",
            ExpiresAt = now.AddDays(-1),
            GeneratedAt = now.AddDays(-10),
            IsActive = false   // <- inactive
        };
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "test.jpg" };

        // Active lookup returns null (no usable cached row).
        _mockUrlRepository
            .Setup(x => x.GetByPhotoAndQualityAsync(photoId, QualityType.Thumbnail))
            .ReturnsAsync((PhotoVersionUrl?)null);

        // Including-inactive lookup returns the inactive row.
        _mockUrlRepository
            .Setup(x => x.GetByPhotoAndQualityIncludingInactiveAsync(photoId, QualityType.Thumbnail))
            .ReturnsAsync(inactiveRow);

        _mockPhotoRepository
            .Setup(x => x.GetByIdAsync(photoId))
            .ReturnsAsync(photo);
        _mockStorageProvider
            .Setup(x => x.ExistsAsync(storageKey))
            .ReturnsAsync(true);
        _mockStorageProvider
            .Setup(x => x.GetUrlAsync(storageKey, It.IsAny<int>()))
            .ReturnsAsync("http://minio/fresh-after-reactivation");

        PhotoVersionUrl? capturedUpdate = null;
        _mockUrlRepository
            .Setup(x => x.UpdateAsync(It.IsAny<PhotoVersionUrl>()))
            .Callback<PhotoVersionUrl>(u => capturedUpdate = u)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.GetPhotoVersionUrlAsync(photoId, QualityType.Thumbnail);

        // Assert: fresh URL returned.
        Assert.Equal("http://minio/fresh-after-reactivation", result);

        // The inactive row was updated in place (not a new row added).
        _mockUrlRepository.Verify(x => x.UpdateAsync(It.IsAny<PhotoVersionUrl>()), Times.AtLeastOnce);
        _mockUrlRepository.Verify(x => x.AddAsync(It.IsAny<PhotoVersionUrl>()), Times.Never);

        // The captured row was reactivated and given the fresh URL.
        Assert.NotNull(capturedUpdate);
        Assert.Equal(inactiveRow.Id, capturedUpdate!.Id);
        Assert.True(capturedUpdate.IsActive, "Row must be reactivated after overwrite.");
        Assert.Equal("http://minio/fresh-after-reactivation", capturedUpdate.PresignedUrl);
    }

    /// <summary>
    /// PR-B / bug #7 regression guard. Even if a buggy caller passes <c>watermarked=true</c>
    /// for <see cref="QualityType.Original"/>, the service must coerce the flag off and
    /// serve the unwatermarked <c>original.jpg</c> object — never a watermarked variant.
    /// </summary>
    [Fact]
    public async Task GenerateShortLivedUrlAsync_Original_NeverServesWatermarkedVariant()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "test.jpg" };
        var originalKey = $"photogallery/{albumId}/{photoId}/original.jpg";
        var watermarkedMediumKey = $"photogallery/{albumId}/{photoId}/medium-watermarked.jpg";

        _mockPhotoRepository
            .Setup(x => x.GetByIdAsync(photoId))
            .ReturnsAsync(photo);

        _mockStorageProvider
            .Setup(x => x.ExistsAsync(originalKey))
            .ReturnsAsync(true);

        _mockStorageProvider
            .Setup(x => x.GetUrlAsync(originalKey, It.IsAny<int>()))
            .ReturnsAsync("http://minio/original-url");

        // Act — caller asks for watermarked Original (which must be refused).
        var result = await _service.GenerateShortLivedUrlAsync(
            photoId, QualityType.Original, ttlMinutes: 15, watermarked: true);

        // Assert: served the unwatermarked Original.
        Assert.Equal("http://minio/original-url", result);

        // The watermarked-medium key must never have been touched.
        _mockStorageProvider.Verify(x => x.ExistsAsync(watermarkedMediumKey), Times.Never);
        _mockStorageProvider.Verify(x => x.GetUrlAsync(watermarkedMediumKey, It.IsAny<int>()), Times.Never);

        // The Original key was the one fetched.
        _mockStorageProvider.Verify(x => x.ExistsAsync(originalKey), Times.Once);
        _mockStorageProvider.Verify(x => x.GetUrlAsync(originalKey, It.IsAny<int>()), Times.Once);
    }

    /// <summary>
    /// Pins the canonical storage-key format for Original — must match the path the
    /// upload pipeline writes (<c>ImageProcessingService.cs</c> :208,
    /// <c>PhotosController.cs</c> :108). Probes through public surface
    /// (<see cref="PhotoVersionUrlService.GenerateShortLivedUrlAsync"/>) so the test
    /// is independent of the internal helper.
    /// </summary>
    [Fact]
    public async Task GenerateShortLivedUrlAsync_Original_UsesUploadPipelineKey()
    {
        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var photo = new Photo { Id = photoId, AlbumId = albumId, FileName = "test.jpg" };
        var expectedKey = $"photogallery/{albumId}/{photoId}/original.jpg";

        _mockPhotoRepository.Setup(x => x.GetByIdAsync(photoId)).ReturnsAsync(photo);
        _mockStorageProvider.Setup(x => x.ExistsAsync(expectedKey)).ReturnsAsync(true);
        _mockStorageProvider
            .Setup(x => x.GetUrlAsync(expectedKey, It.IsAny<int>()))
            .ReturnsAsync("http://minio/original-url");

        var result = await _service.GenerateShortLivedUrlAsync(photoId, QualityType.Original);

        Assert.Equal("http://minio/original-url", result);
        _mockStorageProvider.Verify(x => x.ExistsAsync(expectedKey), Times.Once);
    }
}

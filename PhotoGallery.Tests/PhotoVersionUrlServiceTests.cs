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

        // Create a real configuration object for testing
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"BlobStorage:PreSignedUrlTTLDays", "7"},
            {"BlobStorage:PreSignedUrlRefreshWindowDays", "5"},
            {"BlobStorage:CachedQualities:0", "Thumbnail"},
            {"BlobStorage:CachedQualities:1", "Medium"},
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

    [Fact]
    public async Task GetPhotoVersionUrlAsync_Should_Return_Cached_Url_If_Valid()
    {
        // Arrange
        var photoId = Guid.NewGuid();
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
        Assert.Equal(4, result.Count);  // All 4 qualities
        Assert.Contains(QualityType.Thumbnail, result.Keys);
        Assert.Contains(QualityType.Low, result.Keys);
        Assert.Contains(QualityType.Medium, result.Keys);
        Assert.Contains(QualityType.High, result.Keys);

        // Verify storage was called for each quality
        _mockStorageProvider.Verify(x => x.ExistsAsync(It.IsAny<string>()), Times.Exactly(4));
        _mockStorageProvider.Verify(x => x.GetUrlAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(4));

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
        Assert.Equal(4, result.Count);
        Assert.NotNull(result[QualityType.Thumbnail]);
        Assert.Null(result[QualityType.Low]);
        Assert.Null(result[QualityType.Medium]);
        Assert.Null(result[QualityType.High]);
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
}

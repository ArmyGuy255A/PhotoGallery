using Xunit;
using PhotoGallery.Controllers;
using PhotoGallery.Models;
using PhotoGallery.Enums;

namespace PhotoGallery.Tests;

/// <summary>
/// Unit tests for PhotoListDto with pre-signed URL properties
/// </summary>
public class PhotoListDtoTests
{
    [Fact]
    public void PhotoListDto_Should_Include_Thumbnail_Url()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var thumbnailUrl = "http://minio/thumbnail.jpg";

        // Act
        var dto = new PhotoListDto
        {
            Id = photoId.ToString(),
            FileName = "photo.jpg",
            UploadDate = DateTime.UtcNow,
            UploadedBy = "test-user",
            ThumbnailUrl = thumbnailUrl
        };

        // Assert
        Assert.Equal(thumbnailUrl, dto.ThumbnailUrl);
    }

    [Fact]
    public void PhotoListDto_Should_Include_Medium_Url()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var mediumUrl = "http://minio/medium.jpg";

        // Act
        var dto = new PhotoListDto
        {
            Id = photoId.ToString(),
            FileName = "photo.jpg",
            UploadDate = DateTime.UtcNow,
            UploadedBy = "test-user",
            MediumUrl = mediumUrl
        };

        // Assert
        Assert.Equal(mediumUrl, dto.MediumUrl);
    }

    [Fact]
    public void PhotoListDto_Urls_Can_Be_Null()
    {
        // Arrange & Act
        var dto = new PhotoListDto
        {
            Id = Guid.NewGuid().ToString(),
            FileName = "photo.jpg",
            UploadDate = DateTime.UtcNow,
            UploadedBy = "test-user"
            // URLs not set
        };

        // Assert
        Assert.Null(dto.ThumbnailUrl);
        Assert.Null(dto.MediumUrl);
    }

    [Fact]
    public void PhotoListDto_Should_Preserve_All_Properties()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var fileName = "vacation-photo.jpg";
        var uploadDate = DateTime.UtcNow;
        var uploadedBy = "photographer@example.com";
        var thumbnailUrl = "http://minio/thumb-123.jpg";
        var mediumUrl = "http://minio/medium-123.jpg";

        // Act
        var dto = new PhotoListDto
        {
            Id = photoId.ToString(),
            FileName = fileName,
            UploadDate = uploadDate,
            UploadedBy = uploadedBy,
            ThumbnailUrl = thumbnailUrl,
            MediumUrl = mediumUrl
        };

        // Assert
        Assert.Equal(photoId.ToString(), dto.Id);
        Assert.Equal(fileName, dto.FileName);
        Assert.Equal(uploadDate, dto.UploadDate);
        Assert.Equal(uploadedBy, dto.UploadedBy);
        Assert.Equal(thumbnailUrl, dto.ThumbnailUrl);
        Assert.Equal(mediumUrl, dto.MediumUrl);
    }
}

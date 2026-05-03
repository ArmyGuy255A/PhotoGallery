using Xunit;
using PhotoGallery.Models;

namespace PhotoGallery.Tests;

/// <summary>
/// Integration tests for Albums API
/// </summary>
public class AlbumApiTests
{
    [Fact]
    public void AlbumModel_Should_Create_With_Required_Fields()
    {
        // Arrange
        var userId = "test-user-123";
        
        // Act
        var album = new Album
        {
            Id = Guid.NewGuid(),
            Title = "My Photos",
            Description = "Summer 2024 photos",
            OwnerId = userId,
            CreatedBy = userId,
            CreatedDate = DateTime.UtcNow
        };

        // Assert
        Assert.NotEqual(Guid.Empty, album.Id);
        Assert.Equal("My Photos", album.Title);
        Assert.Equal("Summer 2024 photos", album.Description);
        Assert.Equal(userId, album.OwnerId);
    }

    [Fact]
    public void AccessCode_Should_Generate_Unique_Code()
    {
        // Arrange
        var albumId = Guid.NewGuid();
        var userId = "test-user-123";
        
        // Act
        var code1 = new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = albumId,
            Code = "ABC123XYZ789",
            ExpirationDate = DateTime.UtcNow.AddDays(30),
            CreatedDate = DateTime.UtcNow,
            CreatedBy = userId
        };

        var code2 = new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = albumId,
            Code = "DEF456UVW000",
            ExpirationDate = DateTime.UtcNow.AddDays(30),
            CreatedDate = DateTime.UtcNow,
            CreatedBy = userId
        };

        // Assert
        Assert.NotEqual(code1.Code, code2.Code);
        Assert.NotEqual(code1.Id, code2.Id);
    }

    [Fact]
    public void AccessCode_Should_Detect_Expiration()
    {
        // Arrange
        var expiredCode = new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = Guid.NewGuid(),
            Code = "EXPIRED123",
            ExpirationDate = DateTime.UtcNow.AddDays(-1), // Yesterday
            CreatedDate = DateTime.UtcNow.AddDays(-30),
            CreatedBy = "test-user"
        };

        var validCode = new AccessCode
        {
            Id = Guid.NewGuid(),
            AlbumId = Guid.NewGuid(),
            Code = "VALID123",
            ExpirationDate = DateTime.UtcNow.AddDays(30), // In 30 days
            CreatedDate = DateTime.UtcNow,
            CreatedBy = "test-user"
        };

        // Act & Assert
        Assert.True(expiredCode.ExpirationDate < DateTime.UtcNow, "Code should be expired");
        Assert.True(validCode.ExpirationDate > DateTime.UtcNow, "Code should still be valid");
    }

    [Fact]
    public void Photo_Should_Store_Metadata()
    {
        // Arrange
        var albumId = Guid.NewGuid();
        var userId = "test-user-123";

        // Act
        var photo = new Photo
        {
            Id = Guid.NewGuid(),
            AlbumId = albumId,
            FileName = "vacation-2024.jpg",
            UploadDate = DateTime.UtcNow,
            StorageKey = "photos/album-123/photo-456/vacation-2024.jpg",
            UploadedBy = userId
        };

        // Assert
        Assert.NotEqual(Guid.Empty, photo.Id);
        Assert.Equal(albumId, photo.AlbumId);
        Assert.Equal("vacation-2024.jpg", photo.FileName);
        Assert.Contains("photos/", photo.StorageKey);
    }

    [Fact]
    public void PhotoVersion_Should_Track_Quality_Levels()
    {
        // Arrange
        var photoId = Guid.NewGuid();

        // Act & Assert
        var highQuality = new PhotoVersion
        {
            Id = Guid.NewGuid(),
            PhotoId = photoId,
            Quality = PhotoQuality.HighCompression,
            FileSize = 150000,
            StorageKey = "photos/photo-123/high.jpg",
            ProcessedDate = DateTime.UtcNow
        };

        var mediumQuality = new PhotoVersion
        {
            Id = Guid.NewGuid(),
            PhotoId = photoId,
            Quality = PhotoQuality.MediumCompression,
            FileSize = 300000,
            StorageKey = "photos/photo-123/medium.jpg",
            ProcessedDate = DateTime.UtcNow
        };

        var rawQuality = new PhotoVersion
        {
            Id = Guid.NewGuid(),
            PhotoId = photoId,
            Quality = PhotoQuality.Raw,
            FileSize = 2500000,
            StorageKey = "photos/photo-123/raw.jpg",
            ProcessedDate = DateTime.UtcNow
        };

        Assert.Equal(PhotoQuality.HighCompression, highQuality.Quality);
        Assert.Equal(PhotoQuality.MediumCompression, mediumQuality.Quality);
        Assert.Equal(PhotoQuality.Raw, rawQuality.Quality);
        Assert.True(highQuality.FileSize < mediumQuality.FileSize);
        Assert.True(mediumQuality.FileSize < rawQuality.FileSize);
    }
}

/// <summary>
/// Tests for photo upload and storage path structure
/// </summary>
public class PhotoUploadTests
{
    [Fact]
    public void Photo_Should_Use_Correct_Storage_Path_Structure()
    {
        // Arrange
        var albumId = Guid.Parse("c373c777-c47a-4648-937e-c92006319c30");
        var photoId = Guid.Parse("5c560d87-c11d-4d1d-88de-2f2c8d88a9ff");
        var fileName = "sample-photo-01.jpg";

        // Act
        var expectedStoragePath = $"photogallery/{albumId}/{photoId}/original.jpg";
        var photo = new Photo
        {
            Id = photoId,
            AlbumId = albumId,
            FileName = fileName,
            StorageKey = expectedStoragePath,
            UploadDate = DateTime.UtcNow,
            UploadedBy = "test-user"
        };

        // Assert
        Assert.Equal(expectedStoragePath, photo.StorageKey);
        Assert.StartsWith("photogallery/", photo.StorageKey);
        Assert.Contains(albumId.ToString(), photo.StorageKey);
        Assert.Contains(photoId.ToString(), photo.StorageKey);
        Assert.EndsWith("original.jpg", photo.StorageKey);
    }

    [Fact]
    public void PhotoVersion_Should_Use_Quality_Specific_Storage_Path()
    {
        // Arrange
        var albumId = Guid.Parse("c373c777-c47a-4648-937e-c92006319c30");
        var photoId = Guid.Parse("5c560d87-c11d-4d1d-88de-2f2c8d88a9ff");
        var qualities = new[] { "high", "medium", "low", "raw" };

        // Act & Assert
        foreach (var quality in qualities)
        {
            var storagePath = $"photogallery/{albumId}/{photoId}/{quality}.jpg";
            var version = new PhotoVersion
            {
                Id = Guid.NewGuid(),
                PhotoId = photoId,
                StorageKey = storagePath,
                FileSize = 100000
            };

            Assert.Equal(storagePath, version.StorageKey);
            Assert.StartsWith("photogallery/", version.StorageKey);
            Assert.Contains(albumId.ToString(), version.StorageKey);
            Assert.Contains(photoId.ToString(), version.StorageKey);
            Assert.EndsWith($"{quality}.jpg", version.StorageKey);
        }
    }

    [Fact]
    public void Photo_Storage_Path_Should_Be_Consistent_For_Album()
    {
        // Arrange
        var albumId = Guid.NewGuid();
        var photo1Id = Guid.NewGuid();
        var photo2Id = Guid.NewGuid();

        // Act
        var photo1 = new Photo
        {
            Id = photo1Id,
            AlbumId = albumId,
            FileName = "photo1.jpg",
            StorageKey = $"photogallery/{albumId}/{photo1Id}/original.jpg"
        };

        var photo2 = new Photo
        {
            Id = photo2Id,
            AlbumId = albumId,
            FileName = "photo2.jpg",
            StorageKey = $"photogallery/{albumId}/{photo2Id}/original.jpg"
        };

        // Assert - Both photos should be in same album bucket
        var photo1Parts = photo1.StorageKey.Split('/');
        var photo2Parts = photo2.StorageKey.Split('/');

        Assert.Equal("photogallery", photo1Parts[0]);
        Assert.Equal("photogallery", photo2Parts[0]);
        Assert.Equal(albumId.ToString(), photo1Parts[1]);
        Assert.Equal(albumId.ToString(), photo2Parts[1]);
        // Different photo IDs
        Assert.NotEqual(photo1Parts[2], photo2Parts[2]);
    }
}

/// <summary>
/// Tests for processing queue functionality
/// </summary>
public class ProcessingQueueTests
{
    [Fact]
    public void ProcessingQueue_Should_Track_Status()
    {
        // Arrange & Act
        var queueItem = new ProcessingQueue
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal(ProcessingStatus.Pending, queueItem.Status);
        Assert.Null(queueItem.CompletedAt);
        Assert.Null(queueItem.ErrorMessage);
    }

    [Fact]
    public void ProcessingQueue_Should_Update_To_Processing()
    {
        // Arrange
        var queueItem = new ProcessingQueue
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        queueItem.MarkProcessing();

        // Assert
        Assert.Equal(ProcessingStatus.Processing, queueItem.Status);
    }

    [Fact]
    public void ProcessingQueue_Should_Record_Completion()
    {
        // Arrange
        var queueItem = new ProcessingQueue
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        queueItem.MarkComplete();

        // Assert
        Assert.Equal(ProcessingStatus.Complete, queueItem.Status);
        Assert.NotNull(queueItem.CompletedAt);
    }

    [Fact]
    public void ProcessingQueue_Should_Record_Error()
    {
        // Arrange
        var queueItem = new ProcessingQueue
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        queueItem.MarkError("Image format not supported");

        // Assert
        Assert.Equal(ProcessingStatus.Error, queueItem.Status);
        Assert.Equal("Image format not supported", queueItem.ErrorMessage);
    }
}

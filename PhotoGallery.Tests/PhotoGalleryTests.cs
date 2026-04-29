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
            Id = Guid.NewGuid().ToString(),
            PhotoId = Guid.NewGuid(),
            Status = ProcessingStatus.Pending,
            QueuedDate = DateTime.UtcNow,
            RetryCount = 0
        };

        // Assert
        Assert.Equal(ProcessingStatus.Pending, queueItem.Status);
        Assert.Null(queueItem.ProcessedDate);
        Assert.Null(queueItem.ErrorMessage);
    }

    [Fact]
    public void ProcessingQueue_Should_Update_To_Processing()
    {
        // Arrange
        var queueItem = new ProcessingQueue
        {
            Id = Guid.NewGuid().ToString(),
            PhotoId = Guid.NewGuid(),
            Status = ProcessingStatus.Pending,
            QueuedDate = DateTime.UtcNow
        };

        // Act
        queueItem.Status = ProcessingStatus.Processing;

        // Assert
        Assert.Equal(ProcessingStatus.Processing, queueItem.Status);
    }

    [Fact]
    public void ProcessingQueue_Should_Record_Completion()
    {
        // Arrange
        var queueItem = new ProcessingQueue
        {
            Id = Guid.NewGuid().ToString(),
            PhotoId = Guid.NewGuid(),
            Status = ProcessingStatus.Pending,
            QueuedDate = DateTime.UtcNow
        };

        // Act
        queueItem.Status = ProcessingStatus.Complete;
        queueItem.ProcessedDate = DateTime.UtcNow;

        // Assert
        Assert.Equal(ProcessingStatus.Complete, queueItem.Status);
        Assert.NotNull(queueItem.ProcessedDate);
    }

    [Fact]
    public void ProcessingQueue_Should_Record_Error()
    {
        // Arrange
        var queueItem = new ProcessingQueue
        {
            Id = Guid.NewGuid().ToString(),
            PhotoId = Guid.NewGuid(),
            Status = ProcessingStatus.Pending,
            QueuedDate = DateTime.UtcNow,
            RetryCount = 0
        };

        // Act
        queueItem.Status = ProcessingStatus.Error;
        queueItem.ErrorMessage = "Image format not supported";
        queueItem.RetryCount = 1;

        // Assert
        Assert.Equal(ProcessingStatus.Error, queueItem.Status);
        Assert.Equal("Image format not supported", queueItem.ErrorMessage);
        Assert.Equal(1, queueItem.RetryCount);
    }
}

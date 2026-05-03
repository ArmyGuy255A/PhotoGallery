using Xunit;
using Moq;
using PhotoGallery.Models;
using PhotoGallery.Enums;
using PhotoGallery.Services.Processing;
using PhotoGallery.Interfaces;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Services.Storage;
using Microsoft.Extensions.Logging;

namespace PhotoGallery.Tests;

public class ImageProcessingServiceTests
{
    [Fact]
    public async Task QueuePhotoAsync_Should_Create_Processing_Queue_And_Items()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var mockQueue = new Mock<IProcessingQueueRepository>();
        var mockItem = new Mock<IProcessingQueueItemRepository>();
        var mockStorage = new Mock<IStorageProvider>();
        var mockLogger = new Mock<ILogger<ImageProcessingService>>();

        mockQueue.Setup(r => r.AddAsync(It.IsAny<ProcessingQueue>()))
            .Returns(Task.CompletedTask);
        mockQueue.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);
        mockItem.Setup(r => r.AddAsync(It.IsAny<ProcessingQueueItem>()))
            .Returns(Task.CompletedTask);
        mockItem.Setup(r => r.SaveChangesAsync())
            .Returns(Task.CompletedTask);

        var service = new ImageProcessingService(mockQueue.Object, mockItem.Object, mockStorage.Object, mockLogger.Object);

        // Act
        var queueId = await service.QueuePhotoAsync(photoId);

        // Assert
        Assert.NotEqual(Guid.Empty, Guid.Parse(queueId));
        mockQueue.Verify(r => r.AddAsync(It.IsAny<ProcessingQueue>()), Times.Once);
        mockItem.Verify(r => r.AddAsync(It.IsAny<ProcessingQueueItem>()), Times.Exactly(4)); // 4 qualities
    }

    [Fact]
    public async Task QueuePhotoAsync_Should_Create_Four_Quality_Items()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var mockQueue = new Mock<IProcessingQueueRepository>();
        var mockItem = new Mock<IProcessingQueueItemRepository>();
        var mockStorage = new Mock<IStorageProvider>();
        var mockLogger = new Mock<ILogger<ImageProcessingService>>();

        var addedItems = new List<ProcessingQueueItem>();
        mockQueue.Setup(r => r.AddAsync(It.IsAny<ProcessingQueue>())).Returns(Task.CompletedTask);
        mockQueue.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        mockItem.Setup(r => r.AddAsync(It.IsAny<ProcessingQueueItem>()))
            .Callback<ProcessingQueueItem>(item => addedItems.Add(item))
            .Returns(Task.CompletedTask);
        mockItem.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = new ImageProcessingService(mockQueue.Object, mockItem.Object, mockStorage.Object, mockLogger.Object);

        // Act
        await service.QueuePhotoAsync(photoId);

        // Assert
        Assert.Equal(4, addedItems.Count);
        Assert.Contains(addedItems, item => item.Quality == QualityType.Thumbnail);
        Assert.Contains(addedItems, item => item.Quality == QualityType.Low);
        Assert.Contains(addedItems, item => item.Quality == QualityType.Medium);
        Assert.Contains(addedItems, item => item.Quality == QualityType.High);
    }

    [Fact]
    public async Task GetCompressionProfiles_Should_Return_Four_Profiles()
    {
        // Arrange
        var mockQueue = new Mock<IProcessingQueueRepository>();
        var mockItem = new Mock<IProcessingQueueItemRepository>();
        var mockStorage = new Mock<IStorageProvider>();
        var mockLogger = new Mock<ILogger<ImageProcessingService>>();

        var service = new ImageProcessingService(mockQueue.Object, mockItem.Object, mockStorage.Object, mockLogger.Object);

        // Act
        var profiles = service.GetCompressionProfiles();

        // Assert
        Assert.NotNull(profiles);
        Assert.Equal(4, profiles.Count());
    }
}

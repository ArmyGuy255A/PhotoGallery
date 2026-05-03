using Xunit;
using Moq;
using PhotoGallery.Models;
using PhotoGallery.Enums;
using PhotoGallery.Services.Processing;
using PhotoGallery.Interfaces;
using PhotoGallery.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace PhotoGallery.Tests;

public class PhotoConsistencyCheckerTests
{
    [Fact]
    public async Task VerifyPhotoCompleteAsync_Should_Return_True_For_Complete_Photo()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var queueId = Guid.NewGuid();

        var items = new List<ProcessingQueueItem>
        {
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, Quality = QualityType.Low, Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, Quality = QualityType.Medium, Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, Quality = QualityType.High, Status = ProcessingStatus.Complete }
        };

        var mockQueueRepo = new Mock<IProcessingQueueRepository>();
        var mockItemRepo = new Mock<IProcessingQueueItemRepository>();
        var mockLogger = new Mock<ILogger<PhotoConsistencyChecker>>();

        mockItemRepo.Setup(r => r.GetByPhotoIdAsync(photoId)).ReturnsAsync(items);

        var checker = new PhotoConsistencyChecker(mockQueueRepo.Object, mockItemRepo.Object, mockLogger.Object);

        // Act
        var result = await checker.VerifyPhotoCompleteAsync(photoId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task VerifyPhotoCompleteAsync_Should_Return_False_For_Incomplete_Photo()
    {
        // Arrange
        var photoId = Guid.NewGuid();

        var items = new List<ProcessingQueueItem>
        {
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, Quality = QualityType.Low, Status = ProcessingStatus.Pending },
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, Quality = QualityType.Medium, Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, Quality = QualityType.High, Status = ProcessingStatus.Complete }
        };

        var mockQueueRepo = new Mock<IProcessingQueueRepository>();
        var mockItemRepo = new Mock<IProcessingQueueItemRepository>();
        var mockLogger = new Mock<ILogger<PhotoConsistencyChecker>>();

        mockItemRepo.Setup(r => r.GetByPhotoIdAsync(photoId)).ReturnsAsync(items);

        var checker = new PhotoConsistencyChecker(mockQueueRepo.Object, mockItemRepo.Object, mockLogger.Object);

        // Act
        var result = await checker.VerifyPhotoCompleteAsync(photoId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task MarkQueueCompleteIfReadyAsync_Should_Mark_Queue_Complete()
    {
        // Arrange
        var queueId = Guid.NewGuid();
        var photoId = Guid.NewGuid();

        var queue = new ProcessingQueue { Id = queueId, PhotoId = photoId, Status = ProcessingStatus.Processing };
        var items = new List<ProcessingQueueItem>
        {
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, Quality = QualityType.Low, Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, Quality = QualityType.Medium, Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, Quality = QualityType.High, Status = ProcessingStatus.Complete }
        };

        var mockQueueRepo = new Mock<IProcessingQueueRepository>();
        var mockItemRepo = new Mock<IProcessingQueueItemRepository>();
        var mockLogger = new Mock<ILogger<PhotoConsistencyChecker>>();

        mockQueueRepo.Setup(r => r.GetByIdAsync(queueId)).ReturnsAsync(queue);
        mockItemRepo.Setup(r => r.GetByQueueIdAsync(queueId)).ReturnsAsync(items);
        mockQueueRepo.Setup(r => r.UpdateAsync(It.IsAny<ProcessingQueue>())).Returns(Task.CompletedTask);
        mockQueueRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var checker = new PhotoConsistencyChecker(mockQueueRepo.Object, mockItemRepo.Object, mockLogger.Object);

        // Act
        await checker.MarkQueueCompleteIfReadyAsync(queueId);

        // Assert
        Assert.Equal(ProcessingStatus.Complete, queue.Status);
        mockQueueRepo.Verify(r => r.UpdateAsync(It.IsAny<ProcessingQueue>()), Times.Once);
    }
}

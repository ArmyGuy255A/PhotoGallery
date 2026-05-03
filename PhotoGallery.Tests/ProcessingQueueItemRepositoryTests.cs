using Xunit;
using PhotoGallery.Models;
using PhotoGallery.Enums;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace PhotoGallery.Tests;

public class ProcessingQueueItemRepositoryTests
{
    private ApplicationDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetPendingItemsAsync_Should_Return_Pending_Items()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var photoId = Guid.NewGuid();
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photoId, Status = ProcessingStatus.Pending };
        
        var pendingItem = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queue.Id,
            PhotoId = photoId,
            Quality = QualityType.Low,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        
        var completedItem = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queue.Id,
            PhotoId = photoId,
            Quality = QualityType.Medium,
            Status = ProcessingStatus.Complete,
            CreatedAt = DateTime.UtcNow
        };

        context.ProcessingQueues.Add(queue);
        context.ProcessingQueueItems.AddRange(pendingItem, completedItem);
        await context.SaveChangesAsync();

        var repository = new ProcessingQueueItemRepository(context);

        // Act
        var result = await repository.GetPendingItemsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(QualityType.Low, result.First().Quality);
        Assert.Equal(ProcessingStatus.Pending, result.First().Status);
    }

    [Fact]
    public async Task GetReadyForRetryAsync_Should_Return_Items_Ready_To_Retry()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var photoId = Guid.NewGuid();
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photoId };

        var now = DateTime.UtcNow;
        
        // Item ready for retry (NextRetryTime is in the past)
        var readyItem = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queue.Id,
            PhotoId = photoId,
            Quality = QualityType.Low,
            Status = ProcessingStatus.Error,
            RetryCount = 1,
            NextRetryTime = now.AddSeconds(-5), // 5 seconds ago
            CreatedAt = now
        };

        // Item not ready yet (NextRetryTime is in the future)
        var notReadyItem = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queue.Id,
            PhotoId = photoId,
            Quality = QualityType.Medium,
            Status = ProcessingStatus.Error,
            RetryCount = 1,
            NextRetryTime = now.AddSeconds(10), // 10 seconds from now
            CreatedAt = now
        };

        context.ProcessingQueues.Add(queue);
        context.ProcessingQueueItems.AddRange(readyItem, notReadyItem);
        await context.SaveChangesAsync();

        var repository = new ProcessingQueueItemRepository(context);

        // Act
        var result = await repository.GetReadyForRetryAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(QualityType.Low, result.First().Quality);
    }

    [Fact]
    public async Task GetByQueueIdAsync_Should_Return_All_Items_For_Queue()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var photoId = Guid.NewGuid();
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photoId };
        var otherQueue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = Guid.NewGuid() };

        var items = new[]
        {
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queue.Id, PhotoId = photoId, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Pending, CreatedAt = DateTime.UtcNow },
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queue.Id, PhotoId = photoId, Quality = QualityType.Low, Status = ProcessingStatus.Pending, CreatedAt = DateTime.UtcNow },
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = otherQueue.Id, PhotoId = Guid.NewGuid(), Quality = QualityType.Medium, Status = ProcessingStatus.Pending, CreatedAt = DateTime.UtcNow }
        };

        context.ProcessingQueues.AddRange(queue, otherQueue);
        context.ProcessingQueueItems.AddRange(items);
        await context.SaveChangesAsync();

        var repository = new ProcessingQueueItemRepository(context);

        // Act
        var result = await repository.GetByQueueIdAsync(queue.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());
        Assert.All(result, item => Assert.Equal(queue.Id, item.ProcessingQueueId));
    }

    [Fact]
    public async Task MarkCompleteAsync_Should_Update_Item_Status()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var photoId = Guid.NewGuid();
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photoId };

        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queue.Id,
            PhotoId = photoId,
            Quality = QualityType.Low,
            Status = ProcessingStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };

        context.ProcessingQueues.Add(queue);
        context.ProcessingQueueItems.Add(item);
        await context.SaveChangesAsync();

        var repository = new ProcessingQueueItemRepository(context);
        var itemId = item.Id;

        // Act
        await repository.MarkCompleteAsync(itemId);

        // Assert
        var updated = context.ProcessingQueueItems.First(i => i.Id == itemId);
        Assert.Equal(ProcessingStatus.Complete, updated.Status);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task MarkFailedAsync_Should_Update_Item_Status_And_Error()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var photoId = Guid.NewGuid();
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photoId };

        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queue.Id,
            PhotoId = photoId,
            Quality = QualityType.Medium,
            Status = ProcessingStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };

        context.ProcessingQueues.Add(queue);
        context.ProcessingQueueItems.Add(item);
        await context.SaveChangesAsync();

        var repository = new ProcessingQueueItemRepository(context);
        var itemId = item.Id;
        var errorMsg = "Image format not supported";

        // Act
        await repository.MarkFailedAsync(itemId, errorMsg);

        // Assert
        var updated = context.ProcessingQueueItems.First(i => i.Id == itemId);
        Assert.Equal(ProcessingStatus.Error, updated.Status);
        Assert.Equal(errorMsg, updated.LastError);
    }

    [Fact]
    public async Task GetByPhotoIdAsync_Should_Return_All_Items_For_Photo()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var photoId = Guid.NewGuid();
        var otherPhotoId = Guid.NewGuid();
        
        var queue1 = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photoId };
        var queue2 = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = otherPhotoId };

        var items = new[]
        {
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queue1.Id, PhotoId = photoId, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Pending, CreatedAt = DateTime.UtcNow },
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queue1.Id, PhotoId = photoId, Quality = QualityType.Low, Status = ProcessingStatus.Pending, CreatedAt = DateTime.UtcNow },
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queue2.Id, PhotoId = otherPhotoId, Quality = QualityType.Medium, Status = ProcessingStatus.Pending, CreatedAt = DateTime.UtcNow }
        };

        context.ProcessingQueues.AddRange(queue1, queue2);
        context.ProcessingQueueItems.AddRange(items);
        await context.SaveChangesAsync();

        var repository = new ProcessingQueueItemRepository(context);

        // Act
        var result = await repository.GetByPhotoIdAsync(photoId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());
        Assert.All(result, item => Assert.Equal(photoId, item.PhotoId));
    }

    [Fact]
    public async Task AddAsync_Should_Insert_New_Item()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var photoId = Guid.NewGuid();
        var queue = new ProcessingQueue { Id = Guid.NewGuid(), PhotoId = photoId };

        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queue.Id,
            PhotoId = photoId,
            Quality = QualityType.High,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        context.ProcessingQueues.Add(queue);
        await context.SaveChangesAsync();

        var repository = new ProcessingQueueItemRepository(context);
        var itemId = item.Id;

        // Act
        await repository.AddAsync(item);
        await repository.SaveChangesAsync();

        // Assert
        var saved = context.ProcessingQueueItems.FirstOrDefault(i => i.Id == itemId);
        Assert.NotNull(saved);
        Assert.Equal(photoId, saved.PhotoId);
        Assert.Equal(QualityType.High, saved.Quality);
    }
}

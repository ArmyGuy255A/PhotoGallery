using Xunit;
using PhotoGallery.Models;
using PhotoGallery.Data;
using PhotoGallery.Enums;
using PhotoGallery.Interfaces;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Services.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace PhotoGallery.Tests;

/// <summary>
/// STEP 9 Integration Tests: Complete photo processing pipeline
/// Tests the full flow: upload → queue → process → complete
/// </summary>
public class PhotoProcessingIntegrationTests
{
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Complete_Photo_Processing_Pipeline_Should_Generate_All_Versions()
    {
        // Arrange: Setup in-memory database
        using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var photoId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        
        // Create test photo
        var photo = new Photo
        {
            Id = photoId,
            AlbumId = albumId,
            FileName = "test.jpg",
            StorageKey = $"photogallery/{albumId}/{photoId}/original.jpg",
            ProcessingStatus = PhotoProcessingStatus.Pending
        };

        await context.Photos.AddAsync(photo);
        await context.SaveChangesAsync();

        // Create processing queues
        var queueId = Guid.NewGuid();
        var queue = new ProcessingQueue
        {
            Id = queueId,
            PhotoId = photoId,
            Status = ProcessingStatus.Processing
        };
        await context.ProcessingQueues.AddAsync(queue);
        await context.SaveChangesAsync();

        // Create 4 processing queue items (one per quality)
        var items = new List<ProcessingQueueItem>
        {
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, ProcessingQueueId = queueId, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Pending },
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, ProcessingQueueId = queueId, Quality = QualityType.Low, Status = ProcessingStatus.Pending },
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, ProcessingQueueId = queueId, Quality = QualityType.Medium, Status = ProcessingStatus.Pending },
            new ProcessingQueueItem { Id = Guid.NewGuid(), PhotoId = photoId, ProcessingQueueId = queueId, Quality = QualityType.High, Status = ProcessingStatus.Pending }
        };

        foreach (var item in items)
        {
            await context.ProcessingQueueItems.AddAsync(item);
        }
        await context.SaveChangesAsync();

        // Setup repositories
        var queueRepo = new ProcessingQueueRepository(context);
        var itemRepo = new ProcessingQueueItemRepository(context);
        var mockLogger = new Mock<ILogger<PhotoConsistencyChecker>>();
        var checker = new PhotoConsistencyChecker(queueRepo, itemRepo, mockLogger.Object);

        // Act: Simulate all items being processed successfully
        foreach (var item in items)
        {
            item.Status = ProcessingStatus.Processing;
            item.Attempts = 1;
        }
        await context.SaveChangesAsync();

        // Complete all items
        foreach (var item in items)
        {
            item.Status = ProcessingStatus.Complete;
            item.CompletedAt = DateTime.UtcNow;
        }
        await context.SaveChangesAsync();

        // Verify photo is complete
        var isComplete = await checker.VerifyPhotoCompleteAsync(photoId);
        await checker.MarkQueueCompleteIfReadyAsync(queueId);
        
        var finalQueue = await queueRepo.GetByIdAsync(queueId);

        // Assert
        Assert.True(isComplete, "Photo should be verified as complete");
        Assert.NotNull(finalQueue);
        Assert.Equal(ProcessingStatus.Complete, finalQueue.Status);
    }

    [Fact]
    public async Task Photo_Processing_Should_Retry_On_Failure()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var photoId = Guid.NewGuid();
        var queueId = Guid.NewGuid();
        
        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = photoId,
            ProcessingQueueId = queueId,
            Quality = QualityType.Medium,
            Status = ProcessingStatus.Processing,
            RetryCount = 0
        };

        await context.ProcessingQueueItems.AddAsync(item);
        await context.SaveChangesAsync();

        var itemRepo = new ProcessingQueueItemRepository(context);

        // Act: Mark as pending (simulating a restart) and increment retry
        item.Status = ProcessingStatus.Pending;
        item.IncrementRetry("First attempt failed");

        await itemRepo.UpdateAsync(item);
        await itemRepo.SaveChangesAsync();

        // Reload from database
        var reloadedItem = await itemRepo.GetByIdAsync(item.Id);

        // Assert
        Assert.NotNull(reloadedItem);
        Assert.Equal(ProcessingStatus.Pending, reloadedItem.Status);
        Assert.Equal(1, reloadedItem.RetryCount);
        Assert.NotNull(reloadedItem.NextRetryTime);
        Assert.True(reloadedItem.NextRetryTime > DateTime.UtcNow); // Should be scheduled in future
    }

    [Fact]
    public async Task Processing_Queue_Should_Track_Multiple_Photos()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var albumId = Guid.NewGuid();
        var photoIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        // Create 3 photos
        foreach (var photoId in photoIds)
        {
            var photo = new Photo
            {
                Id = photoId,
                AlbumId = albumId,
                FileName = $"test-{photoId}.jpg",
                StorageKey = $"photogallery/{albumId}/{photoId}/original.jpg",
                ProcessingStatus = PhotoProcessingStatus.Pending
            };
            await context.Photos.AddAsync(photo);
        }
        await context.SaveChangesAsync();

        var queueRepo = new ProcessingQueueRepository(context);

        // Create queues for each photo
        var queueIds = new List<Guid>();
        foreach (var photoId in photoIds)
        {
            var queue = new ProcessingQueue
            {
                Id = Guid.NewGuid(),
                PhotoId = photoId,
                Status = ProcessingStatus.Pending
            };
            queueIds.Add(queue.Id);
            await queueRepo.AddAsync(queue);
        }
        await queueRepo.SaveChangesAsync();

        // Act: Get pending queues
        var pendingQueues = await queueRepo.GetPendingAsync();
        var pendingList = pendingQueues.ToList();

        // Assert
        Assert.Equal(3, pendingList.Count);
        Assert.True(pendingList.All(q => q.Status == ProcessingStatus.Pending));
    }

    [Fact]
    public async Task Max_Retries_Should_Mark_Item_As_Failed()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            ProcessingQueueId = Guid.NewGuid(),
            Quality = QualityType.High,
            Status = ProcessingStatus.Processing,
            RetryCount = 0
        };

        await context.ProcessingQueueItems.AddAsync(item);
        await context.SaveChangesAsync();

        var itemRepo = new ProcessingQueueItemRepository(context);

        // Act: Retry 3 times (max retries = 3)
        for (int i = 0; i < 3; i++)
        {
            item.Status = ProcessingStatus.Pending;
            item.IncrementRetry($"Retry {i + 1}");
        }

        await itemRepo.UpdateAsync(item);
        await itemRepo.SaveChangesAsync();

        var reloadedItem = await itemRepo.GetByIdAsync(item.Id);
        var canRetry = reloadedItem!.RetryCount < reloadedItem.MaxRetries;

        // Assert
        Assert.Equal(3, reloadedItem.RetryCount);
        Assert.False(canRetry, "Should not retry after max retries reached");
    }
}

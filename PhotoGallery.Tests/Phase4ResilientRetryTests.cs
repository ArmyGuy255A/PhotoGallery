using Microsoft.EntityFrameworkCore;
using Moq;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Enums;
using PhotoGallery.Models;
using PhotoGallery.Services.Processing;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// Phase 4 acceptance tests: bounded-backoff cap, lease-pickup semantics, and the
/// Watermark queue-item enqueue flow. The IncrementRetry / cap-at-1024s assertions
/// live in <see cref="ProcessingQueueItemModelTests"/>; this file covers the new
/// repository + service surfaces introduced for Phase 4.
/// </summary>
public class Phase4ResilientRetryTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    // ---- Lease pickup ----

    [Fact]
    public async Task LeaseNextBatchAsync_Should_Skip_Items_With_Active_Lease()
    {
        // Phase 4 §4: a row with LeaseExpiresAt in the future is "owned" by another
        // worker and must not be returned to a competing pickup call. Active lease →
        // skipped; expired lease → eligible.
        using var context = NewContext();
        var queueId = Guid.NewGuid();
        var photoId = Guid.NewGuid();

        var leasedActive = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queueId,
            PhotoId = photoId,
            Quality = QualityType.Thumbnail,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddSeconds(-30),
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(3), // still owned
        };

        var leasedExpired = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queueId,
            PhotoId = photoId,
            Quality = QualityType.Low,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddSeconds(-20),
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1), // expired — fair game
        };

        var unleased = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queueId,
            PhotoId = photoId,
            Quality = QualityType.Medium,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddSeconds(-10),
        };

        context.ProcessingQueueItems.AddRange(leasedActive, leasedExpired, unleased);
        await context.SaveChangesAsync();

        var repo = new ProcessingQueueItemRepository(context);

        var leased = await repo.LeaseNextBatchAsync(10, TimeSpan.FromMinutes(5));

        Assert.Equal(2, leased.Count);
        Assert.Contains(leased, i => i.Id == leasedExpired.Id);
        Assert.Contains(leased, i => i.Id == unleased.Id);
        Assert.DoesNotContain(leased, i => i.Id == leasedActive.Id);

        // The leased rows should now carry a fresh future LeaseExpiresAt.
        Assert.All(leased, i => Assert.NotNull(i.LeaseExpiresAt));
        Assert.All(leased, i => Assert.True(i.LeaseExpiresAt > DateTime.UtcNow));
    }

    [Fact]
    public async Task LeaseNextBatchAsync_Should_Include_Error_Items_With_Retry_Time_Elapsed()
    {
        // Phase 4 §1: GetPendingItems / LeaseNextBatch must also return Error items
        // whose NextRetryTime has come due — no max-retry dead-letter.
        using var context = NewContext();
        var queueId = Guid.NewGuid();
        var photoId = Guid.NewGuid();

        var errorDue = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queueId,
            PhotoId = photoId,
            Quality = QualityType.Thumbnail,
            Status = ProcessingStatus.Error,
            RetryCount = 5,
            NextRetryTime = DateTime.UtcNow.AddSeconds(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        };
        var errorWaiting = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            ProcessingQueueId = queueId,
            PhotoId = photoId,
            Quality = QualityType.Low,
            Status = ProcessingStatus.Error,
            RetryCount = 3,
            NextRetryTime = DateTime.UtcNow.AddMinutes(5), // not yet due
            CreatedAt = DateTime.UtcNow.AddMinutes(-9),
        };

        context.ProcessingQueueItems.AddRange(errorDue, errorWaiting);
        await context.SaveChangesAsync();

        var repo = new ProcessingQueueItemRepository(context);
        var leased = await repo.LeaseNextBatchAsync(10, TimeSpan.FromMinutes(5));

        Assert.Single(leased);
        Assert.Equal(errorDue.Id, leased[0].Id);
    }

    // ---- Watermark enqueue after all 4 base qualities complete ----

    [Fact]
    public async Task EnqueueWatermarkIfMissing_Should_Add_Single_Watermark_Item()
    {
        using var context = NewContext();
        var queueId = Guid.NewGuid();
        var photoId = Guid.NewGuid();

        var existing = new[]
        {
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queueId, PhotoId = photoId, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queueId, PhotoId = photoId, Quality = QualityType.Low,       Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queueId, PhotoId = photoId, Quality = QualityType.Medium,    Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queueId, PhotoId = photoId, Quality = QualityType.High,      Status = ProcessingStatus.Complete },
        };
        context.ProcessingQueueItems.AddRange(existing);
        await context.SaveChangesAsync();

        var repo = new ProcessingQueueItemRepository(context);
        await ImageProcessingService.EnqueueWatermarkIfMissingAsync(repo, queueId, photoId, existing);

        var allItems = await context.ProcessingQueueItems
            .Where(i => i.ProcessingQueueId == queueId)
            .ToListAsync();

        Assert.Equal(5, allItems.Count);
        var watermarkItems = allItems.Where(i => i.Quality == QualityType.Watermark).ToList();
        Assert.Single(watermarkItems);
        Assert.Equal(ProcessingStatus.Pending, watermarkItems[0].Status);
        Assert.Equal(photoId, watermarkItems[0].PhotoId);
    }

    [Fact]
    public async Task EnqueueWatermarkIfMissing_Should_Be_Idempotent_When_Already_Present()
    {
        using var context = NewContext();
        var queueId = Guid.NewGuid();
        var photoId = Guid.NewGuid();

        var existing = new[]
        {
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queueId, PhotoId = photoId, Quality = QualityType.Thumbnail, Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queueId, PhotoId = photoId, Quality = QualityType.Low,       Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queueId, PhotoId = photoId, Quality = QualityType.Medium,    Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queueId, PhotoId = photoId, Quality = QualityType.High,      Status = ProcessingStatus.Complete },
            new ProcessingQueueItem { Id = Guid.NewGuid(), ProcessingQueueId = queueId, PhotoId = photoId, Quality = QualityType.Watermark, Status = ProcessingStatus.Pending  },
        };
        context.ProcessingQueueItems.AddRange(existing);
        await context.SaveChangesAsync();

        var repo = new ProcessingQueueItemRepository(context);
        await ImageProcessingService.EnqueueWatermarkIfMissingAsync(repo, queueId, photoId, existing);

        var watermarkItems = await context.ProcessingQueueItems
            .Where(i => i.ProcessingQueueId == queueId && i.Quality == QualityType.Watermark)
            .ToListAsync();

        Assert.Single(watermarkItems);
    }
}

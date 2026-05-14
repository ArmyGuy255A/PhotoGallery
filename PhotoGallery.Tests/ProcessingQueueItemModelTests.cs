using Xunit;
using PhotoGallery.Models;
using PhotoGallery.Enums;

namespace PhotoGallery.Tests;

public class ProcessingQueueItemModelTests
{
    [Fact]
    public void ProcessingQueueItem_Should_Create_With_Pending_Status()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        var quality = QualityType.Low;

        // Act
        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = photoId,
            Quality = quality,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.Equal(photoId, item.PhotoId);
        Assert.Equal(quality, item.Quality);
        Assert.Equal(ProcessingStatus.Pending, item.Status);
        Assert.Equal(0, item.RetryCount);
        Assert.True(item.CanRetry);
        Assert.Null(item.LastError);
    }

    [Fact]
    public void ProcessingQueueItem_Should_Support_All_Quality_Types()
    {
        // Arrange & Act & Assert
        var thumbnail = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Quality = QualityType.Thumbnail,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        Assert.Equal(QualityType.Thumbnail, thumbnail.Quality);

        var low = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Quality = QualityType.Low,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        Assert.Equal(QualityType.Low, low.Quality);

        var medium = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Quality = QualityType.Medium,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        Assert.Equal(QualityType.Medium, medium.Quality);

        var high = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Quality = QualityType.High,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        Assert.Equal(QualityType.High, high.Quality);
    }

    [Fact]
    public void ProcessingQueueItem_Should_Track_Retry_Count()
    {
        // Arrange
        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Quality = QualityType.Low,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            RetryCount = 0
        };

        // Act
        item.IncrementRetry("First attempt failed");

        // Assert
        Assert.Equal(1, item.RetryCount);
        Assert.Equal("First attempt failed", item.LastError);
        Assert.True(item.CanRetry);
    }

    [Fact]
    public void ProcessingQueueItem_Should_Calculate_Exponential_Backoff()
    {
        // Arrange
        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Quality = QualityType.Low,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            RetryCount = 0
        };

        // Act - First retry: 2^1 = 2 seconds
        item.IncrementRetry("Error 1");
        var beforeSecondRetry = DateTime.UtcNow;
        var firstRetryTime = item.NextRetryTime;

        // Assert
        Assert.Equal(1, item.RetryCount);
        Assert.NotNull(item.NextRetryTime);
        // Should be approximately 2 seconds from now (allow ±500ms for test execution)
        var timeDiff1 = (firstRetryTime!.Value - DateTime.UtcNow).TotalSeconds;
        Assert.True(timeDiff1 > 1.5 && timeDiff1 < 2.5, $"First retry backoff should be ~2 seconds, got {timeDiff1}s");

        // Act - Second retry: 2^2 = 4 seconds
        item.IncrementRetry("Error 2");
        var secondRetryTime = item.NextRetryTime;

        // Assert
        Assert.Equal(2, item.RetryCount);
        Assert.NotNull(item.NextRetryTime);
        // Should be approximately 4 seconds from when we started this increment
        var timeDiff2 = (secondRetryTime.Value - beforeSecondRetry).TotalSeconds;
        Assert.True(timeDiff2 > 3.5 && timeDiff2 < 4.5, $"Second retry backoff should be ~4 seconds, got {timeDiff2}s");

        // Act - Third retry: 2^3 = 8 seconds
        item.IncrementRetry("Error 3");
        var thirdRetryTime = item.NextRetryTime;

        // Assert
        Assert.Equal(3, item.RetryCount);
        Assert.NotNull(item.NextRetryTime);
        // Should be approximately 8 seconds from now (allow ±1s for test execution since this is the longest wait)
        var timeDiff3 = (thirdRetryTime.Value - DateTime.UtcNow).TotalSeconds;
        Assert.True(timeDiff3 > 7 && timeDiff3 < 9, $"Third retry backoff should be ~8 seconds, got {timeDiff3}s");
    }

    [Fact]
    public void ProcessingQueueItem_Should_Always_Allow_Retry()
    {
        // Phase 4 §1: CanRetry always returns true — there is no max-retry dead-letter.
        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Quality = QualityType.Low,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            RetryCount = 0
        };

        item.IncrementRetry("Error 1");
        Assert.Equal(1, item.RetryCount);
        Assert.True(item.CanRetry);

        item.IncrementRetry("Error 2");
        Assert.Equal(2, item.RetryCount);
        Assert.True(item.CanRetry);

        // Drive past the historical "3 strikes" cap.
        for (int i = 0; i < 25; i++)
            item.IncrementRetry($"Error {i + 3}");

        Assert.Equal(27, item.RetryCount);
        Assert.True(item.CanRetry, "Every item must remain retryable under Phase 4");
    }

    [Fact]
    public void IncrementRetry_Should_Cap_Backoff_At_1024_Seconds()
    {
        // Phase 4 §1: backoff curve = min(2^retryCount, 1024) seconds. Caps at ~17 min
        // so a permanently broken item retries hourly forever instead of clogging
        // the worker with sub-second retries OR waiting weeks between attempts.
        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Quality = QualityType.High,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };

        // RetryCount = 10  →  2^10 = 1024 seconds (right at the cap)
        for (int i = 0; i < 10; i++)
            item.IncrementRetry("transient");

        Assert.NotNull(item.NextRetryTime);
        var seconds10 = (item.NextRetryTime!.Value - DateTime.UtcNow).TotalSeconds;
        Assert.InRange(seconds10, 1020, 1030);

        // Crank to 30 retries — 2^30 would be ~34 years if uncapped. Should still cap at 1024s.
        for (int i = 0; i < 20; i++)
            item.IncrementRetry("transient");

        var seconds30 = (item.NextRetryTime!.Value - DateTime.UtcNow).TotalSeconds;
        Assert.True(seconds30 <= 1024 + 5, $"Backoff at 30 retries should still cap at ~1024s, got {seconds30}s");
    }

    [Fact]
    public void ProcessingQueueItem_Should_Update_Status()
    {
        // Arrange
        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Quality = QualityType.Medium,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        item.Status = ProcessingStatus.Processing;

        // Assert
        Assert.Equal(ProcessingStatus.Processing, item.Status);

        // Act
        item.Status = ProcessingStatus.Complete;

        // Assert
        Assert.Equal(ProcessingStatus.Complete, item.Status);

        // Act
        item.Status = ProcessingStatus.Error;

        // Assert
        Assert.Equal(ProcessingStatus.Error, item.Status);
    }

    [Fact]
    public void ProcessingQueueItem_Should_Track_Completion_Time()
    {
        // Arrange
        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Quality = QualityType.High,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var completionTime = DateTime.UtcNow;
        item.CompletedAt = completionTime;
        item.Status = ProcessingStatus.Complete;

        // Assert
        Assert.NotNull(item.CompletedAt);
        Assert.Equal(completionTime, item.CompletedAt);
    }

    [Fact]
    public void ProcessingQueueItem_Should_Store_Error_Message()
    {
        // Arrange
        var errorMsg = "Image dimensions too small (min 200x200)";
        var item = new ProcessingQueueItem
        {
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Quality = QualityType.Thumbnail,
            Status = ProcessingStatus.Error,
            CreatedAt = DateTime.UtcNow,
            LastError = errorMsg
        };

        // Act & Assert
        Assert.Equal(errorMsg, item.LastError);
    }
}

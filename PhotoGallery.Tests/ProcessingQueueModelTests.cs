using Xunit;
using PhotoGallery.Models;
using PhotoGallery.Enums;
using System;

namespace PhotoGallery.Tests;

/// <summary>
/// ProcessingQueue Model Tests (D003 - Image Processing with Compression Profiles)
/// 
/// Tests validate the ProcessingQueue entity which tracks overall photo processing jobs.
/// Each ProcessingQueue has:
/// - One PhotoId (FK to Photo being processed)
/// - One overall Status (Pending → Processing → Complete → Error)
/// - Multiple ProcessingQueueItems (one per quality: Thumbnail, Low, Medium, High)
/// 
/// Reference: Documentation/Architecture/DESIGN_DECISIONS.md (D003)
/// </summary>
public class ProcessingQueueModelTests
{
    [Fact]
    public void Constructor_CreatesQueueWithPendingStatus()
    {
        // Arrange
        var photoId = Guid.NewGuid();
        
        // Act
        var queue = new ProcessingQueue 
        { 
            Id = Guid.NewGuid(),
            PhotoId = photoId,
            CreatedAt = DateTime.UtcNow
        };
        
        // Assert
        Assert.Equal(photoId, queue.PhotoId);
        Assert.Equal(ProcessingStatus.Pending, queue.Status);
        Assert.Null(queue.CompletedAt);
        Assert.Null(queue.ErrorMessage);
        Assert.NotNull(queue.Items);
        Assert.Empty(queue.Items);
    }
    
    [Fact]
    public void MarkProcessing_ChangesStatusToProcesing()
    {
        // Arrange
        var queue = new ProcessingQueue 
        { 
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        
        // Act
        queue.MarkProcessing();
        
        // Assert
        Assert.Equal(ProcessingStatus.Processing, queue.Status);
    }
    
    [Fact]
    public void MarkComplete_SetsStatusAndCompletedTime()
    {
        // Arrange
        var queue = new ProcessingQueue 
        { 
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Status = ProcessingStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };
        var beforeCompletion = DateTime.UtcNow;
        
        // Act
        queue.MarkComplete();
        var afterCompletion = DateTime.UtcNow;
        
        // Assert
        Assert.Equal(ProcessingStatus.Complete, queue.Status);
        Assert.NotNull(queue.CompletedAt);
        Assert.True(queue.CompletedAt >= beforeCompletion);
        Assert.True(queue.CompletedAt <= afterCompletion);
    }
    
    [Fact]
    public void MarkError_SetsStatusAndErrorMessage()
    {
        // Arrange
        var queue = new ProcessingQueue 
        { 
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            Status = ProcessingStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };
        var errorMessage = "Disk space exceeded";
        
        // Act
        queue.MarkError(errorMessage);
        
        // Assert
        Assert.Equal(ProcessingStatus.Error, queue.Status);
        Assert.Equal(errorMessage, queue.ErrorMessage);
    }
    
    [Fact]
    public void ProcessingQueue_HasNavigationToItems()
    {
        // Arrange
        var queue = new ProcessingQueue 
        { 
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            Items = new List<ProcessingQueueItem>()
        };
        
        // Act
        var item1 = new ProcessingQueueItem 
        { 
            Id = Guid.NewGuid(),
            ProcessingQueueId = queue.Id,
            Quality = QualityType.Thumbnail,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        queue.Items.Add(item1);
        
        // Assert
        Assert.Single(queue.Items);
        Assert.Equal(item1.Id, queue.Items.First().Id);
        Assert.Equal(QualityType.Thumbnail, queue.Items.First().Quality);
    }
    
    [Fact]
    public void ProcessingQueue_StatusTransitionFromPendingThroughCompletion()
    {
        // Arrange
        var queue = new ProcessingQueue 
        { 
            Id = Guid.NewGuid(),
            PhotoId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
        
        // Act & Assert - Pending (initial)
        Assert.Equal(ProcessingStatus.Pending, queue.Status);
        
        // Act & Assert - Pending → Processing
        queue.MarkProcessing();
        Assert.Equal(ProcessingStatus.Processing, queue.Status);
        
        // Act & Assert - Processing → Complete
        queue.MarkComplete();
        Assert.Equal(ProcessingStatus.Complete, queue.Status);
        Assert.NotNull(queue.CompletedAt);
    }
}

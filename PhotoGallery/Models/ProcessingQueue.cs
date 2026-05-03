namespace PhotoGallery.Models;

using PhotoGallery.Enums;
using System.Collections.Generic;

/// <summary>
/// Represents an overall photo processing job that tracks the complete lifecycle
/// of processing a photo into multiple quality versions.
/// 
/// Reference: D003 (Image Processing with Compression Profiles)
/// 
/// One ProcessingQueue = One photo being processed
/// One ProcessingQueue has 4 ProcessingQueueItems (one per quality: Thumbnail, Low, Medium, High)
/// Each ProcessingQueueItem tracks the processing status of a single quality version.
/// </summary>
public class ProcessingQueue
{
    /// <summary>Unique identifier for this processing job</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>Foreign key to the Photo being processed</summary>
    public Guid PhotoId { get; set; }
    
    /// <summary>Navigation property to the Photo being processed</summary>
    public Photo? Photo { get; set; }
    
    /// <summary>Overall status of the processing job (Pending → Processing → Complete → Error)</summary>
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
    
    /// <summary>When this job was created (photo uploaded)</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>When processing completed (all qualities done) - null if still processing</summary>
    public DateTime? CompletedAt { get; set; }
    
    /// <summary>Error message if processing failed - null if successful</summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>The 4 individual quality items being processed (Thumbnail, Low, Medium, High)</summary>
    public ICollection<ProcessingQueueItem> Items { get; set; } = new List<ProcessingQueueItem>();
    
    /// <summary>Mark this job as currently being processed</summary>
    public void MarkProcessing()
    {
        Status = ProcessingStatus.Processing;
    }
    
    /// <summary>Mark this job as complete and set the completion time</summary>
    public void MarkComplete()
    {
        Status = ProcessingStatus.Complete;
        CompletedAt = DateTime.UtcNow;
    }
    
    /// <summary>Mark this job as failed with an error message</summary>
    public void MarkError(string message)
    {
        Status = ProcessingStatus.Error;
        ErrorMessage = message;
    }
}

/// <summary>
/// Status of a processing job or individual quality processing.
/// Reference: D003 (Image Processing with Compression Profiles)
/// </summary>
public enum ProcessingStatus
{
    /// <summary>Waiting to be processed</summary>
    Pending = 0,
    
    /// <summary>Currently being processed</summary>
    Processing = 1,
    
    /// <summary>Processing completed successfully</summary>
    Complete = 2,
    
    /// <summary>Processing failed</summary>
    Error = 3
}

/// <summary>
/// Represents processing of a single quality version of a photo.
/// One ProcessingQueue has 4 ProcessingQueueItems (Thumbnail, Low, Medium, High).
/// Each item tracks individual retry attempts and status.
/// 
/// Reference: D003 (Image Processing with Compression Profiles)
/// </summary>
public class ProcessingQueueItem
{
    /// <summary>Unique identifier for this quality processing item</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>Foreign key to the parent ProcessingQueue</summary>
    public Guid ProcessingQueueId { get; set; }
    
    /// <summary>Navigation property to the parent ProcessingQueue</summary>
    public ProcessingQueue ProcessingQueue { get; set; } = null!;
    
    /// <summary>Which quality version this item represents (Thumbnail, Low, Medium, High)</summary>
    public QualityType Quality { get; set; }
    
    /// <summary>Status of this specific quality processing (Pending → Processing → Complete → Error)</summary>
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
    
    /// <summary>How many times this item has been retried after failure</summary>
    public int RetryCount { get; set; } = 0;
    
    /// <summary>Maximum number of retries allowed (default 3)</summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>Error message from the last failed attempt</summary>
    public string? LastError { get; set; }
    
    /// <summary>Total number of processing attempts (includes retries)</summary>
    public int Attempts { get; set; } = 0;
    
    /// <summary>When this item should be retried next (if failed)</summary>
    public DateTime? NextRetryTime { get; set; }
    
    /// <summary>When this item was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>When this item was last updated</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>Can this item be retried, or have we exceeded max retries?</summary>
    public bool CanRetry => RetryCount < MaxRetries;
    
    /// <summary>Increment retry count and calculate exponential backoff for next retry</summary>
    public void IncrementRetry()
    {
        RetryCount++;
        if (CanRetry)
        {
            // Exponential backoff: 2^retryCount seconds
            // Retry 1: 2 seconds
            // Retry 2: 4 seconds
            // Retry 3: 8 seconds
            var secondsToWait = Math.Pow(2, RetryCount);
            NextRetryTime = DateTime.UtcNow.AddSeconds(secondsToWait);
        }
    }
}

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
    
    /// <summary>Foreign key to the Photo being processed (direct link)</summary>
    public Guid PhotoId { get; set; }
    
    /// <summary>Navigation property to the Photo being processed</summary>
    public Photo? Photo { get; set; }
    
    /// <summary>Foreign key to the parent ProcessingQueue</summary>
    public Guid ProcessingQueueId { get; set; }
    
    /// <summary>Navigation property to the parent ProcessingQueue</summary>
    public ProcessingQueue ProcessingQueue { get; set; } = null!;
    
    /// <summary>Which quality version this item represents (Thumbnail, Low, Medium, High)</summary>
    public QualityType Quality { get; set; }
    
    /// <summary>Status of this specific quality processing (Pending → Processing → Complete → Error)</summary>
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
    
    /// <summary>How many times this item has been retried after failure (observability + backoff curve)</summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>Error message from the last failed attempt</summary>
    public string? LastError { get; set; }

    /// <summary>Total number of processing attempts (includes retries)</summary>
    public int Attempts { get; set; } = 0;

    /// <summary>When this item should be retried next (if failed)</summary>
    public DateTime? NextRetryTime { get; set; }

    /// <summary>
    /// Until when a worker has claimed this item. NULL = unclaimed. A row is eligible
    /// for pickup when <c>LeaseExpiresAt IS NULL OR LeaseExpiresAt &lt; GETUTCDATE()</c>.
    /// Used to prevent two concurrent workers (in-instance via parallelism or
    /// cross-instance once ACA replicas scale out) from picking the same row.
    /// Reference: Phase 4 scope §4 (DB-level lease).
    /// </summary>
    public DateTime? LeaseExpiresAt { get; set; }

    /// <summary>When this item was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this item was completed successfully</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>When this item was last updated</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Legacy compatibility getter: every quality must eventually succeed under the
    /// Phase 4 resilient-retry model. Always returns <c>true</c>. Reference: Phase 4 scope §1.
    /// </summary>
    public bool CanRetry => true;

    /// <summary>
    /// Increment retry count and calculate the next exponential-backoff retry time.
    /// Curve: <c>min(2^retryCount, 1024)</c> seconds. Caps at ~17 min so a permanently
    /// broken item retries on roughly an hour boundary forever instead of clogging the worker.
    /// Reference: Phase 4 scope §1 (backoff cap).
    /// </summary>
    /// <param name="errorMessage">The error message from the failed attempt</param>
    public void IncrementRetry(string errorMessage)
    {
        LastError = errorMessage;
        RetryCount++;

        // Bounded exponential backoff. Math.Pow blows up past ~retryCount=30 (Infinity),
        // so the inner Math.Min capping the seconds keeps the result finite + DB-safe.
        var secondsToWait = Math.Min(Math.Pow(2, RetryCount), 1024);
        NextRetryTime = DateTime.UtcNow.AddSeconds(secondsToWait);
    }
}

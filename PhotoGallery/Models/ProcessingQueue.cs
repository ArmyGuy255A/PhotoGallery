namespace PhotoGallery.Models;

/// <summary>
/// Represents a photo awaiting processing or currently being processed
/// </summary>
public class ProcessingQueue
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    public Guid PhotoId { get; set; }
    
    public Photo? Photo { get; set; }
    
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
    
    public DateTime QueuedDate { get; set; } = DateTime.UtcNow;
    
    public DateTime? ProcessedDate { get; set; }
    
    public string? ErrorMessage { get; set; }
    
    public int RetryCount { get; set; } = 0;
}

public enum ProcessingStatus
{
    Pending = 0,
    Processing = 1,
    Complete = 2,
    Error = 3
}

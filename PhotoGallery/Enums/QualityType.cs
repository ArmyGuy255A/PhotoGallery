namespace PhotoGallery.Enums;

/// <summary>
/// Photo compression quality types for image processing.
/// Reference: D003 (Image Processing with Compression Profiles)
/// 
/// - Thumbnail: 200x200 (very small, fast download, preview only)
/// - Low: 800x800 (mobile viewing)
/// - Medium: 1920x1920 (web/email)
/// - High: 3840x3840 (high resolution, print-ready)
/// - Original: untouched source resolution (paid checkout / archival download)
///
/// Numeric values are kept in lock-step with <see cref="PhotoGallery.Models.PhotoFileQuality"/>
/// so the two enums can be cast back and forth without drift.
/// </summary>
public enum QualityType
{
    /// <summary>Thumbnail size: 200x200px, for UI previews</summary>
    Thumbnail = 0,

    /// <summary>Low quality: 800x800px, for mobile viewing</summary>
    Low = 1,

    /// <summary>Medium quality: 1920x1920px, for web and email</summary>
    Medium = 2,

    /// <summary>High quality: 3840x3840px, for print and high-resolution downloads</summary>
    High = 3,

    /// <summary>
    /// Original (untouched source) resolution. Used by paid-checkout / archival
    /// download flows. Storage key: <c>{albumId}/{photoId}/original.jpg</c>
    /// (the same object the upload pipeline writes). Never watermarked.
    /// </summary>
    Original = 4,

    /// <summary>
    /// Watermark-rendering pseudo-quality. Not a resize step. When a ProcessingQueueItem
    /// with this Quality is processed, the worker renders watermarked variants of the
    /// Thumbnail + Medium qualities into storage (<c>thumbnail-watermarked.jpg</c>,
    /// <c>medium-watermarked.jpg</c>). Enqueued exactly once per photo, after all four
    /// base qualities (Thumbnail/Low/Medium/High) finish. Reference: Phase 4 scope §2
    /// (watermark gets its own queue item) + D009 (Watermark Pipeline).
    /// </summary>
    Watermark = 5
}

namespace PhotoGallery.Enums;

/// <summary>
/// Photo compression quality types for image processing.
/// Reference: D003 (Image Processing with Compression Profiles)
/// 
/// - Thumbnail: 200x200 (very small, fast download, preview only)
/// - Low: 800x800 (mobile viewing)
/// - Medium: 1920x1920 (web/email)
/// - High: 3840x3840 (high resolution, print-ready)
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
    High = 3
}

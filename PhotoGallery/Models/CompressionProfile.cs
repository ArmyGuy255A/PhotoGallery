namespace PhotoGallery.Models;

/// <summary>
/// Image compression quality profile for photo processing
/// </summary>
public class CompressionProfile
{
    public string Name { get; set; } = string.Empty;
    
    public int QualityPercentage { get; set; }
    
    public string Description { get; set; } = string.Empty;
}

using PhotoGallery.Enums;

namespace PhotoGallery.Models;

/// <summary>
/// Represents a download analytics event. Logged when a photo version is delivered to a client.
/// 
/// Used for:
/// - Analytics (which qualities are most downloaded)
/// - Audit (when did a client download what)
/// - Abuse detection (excessive downloads from a single IP)
/// 
/// PII protection: IpHash is a SHA256 hash of the remote IP, never stored in plain text.
/// </summary>
public class Download
{
    public Guid Id { get; set; }

    /// <summary>Foreign key to Photo</summary>
    public Guid PhotoId { get; set; }

    /// <summary>
    /// Foreign key to AccessCode used for download.
    /// Null when downloaded by authenticated owner/admin (not via public access code).
    /// </summary>
    public Guid? AccessCodeId { get; set; }

    /// <summary>Quality version that was downloaded</summary>
    public QualityType Quality { get; set; }

    /// <summary>UTC datetime when the download occurred</summary>
    public DateTime DownloadedAt { get; set; }

    /// <summary>SHA256 hash (hex) of remote IP address. 64 chars. Used for analytics without PII.</summary>
    public string IpHash { get; set; } = string.Empty;

    /// <summary>Navigation property to Photo</summary>
    public Photo? Photo { get; set; }

    /// <summary>Navigation property to AccessCode (may be null)</summary>
    public AccessCode? AccessCode { get; set; }
}

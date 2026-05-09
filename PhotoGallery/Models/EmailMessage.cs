namespace PhotoGallery.Models;

/// <summary>
/// Represents a transactional email message dispatched via <see cref="Interfaces.IEmailService"/>.
/// </summary>
public class EmailMessage
{
    /// <summary>
    /// Recipient email address (single address; multi-recipient is not currently supported).
    /// </summary>
    public string To { get; set; } = string.Empty;

    /// <summary>
    /// Subject line of the email.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// HTML body content of the email.
    /// </summary>
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>
    /// Optional plain-text body. When null, the HTML body is the only payload sent.
    /// </summary>
    public string? TextBody { get; set; }

    /// <summary>
    /// Optional metadata tags forwarded to the underlying provider (e.g. for tracking/analytics).
    /// </summary>
    public Dictionary<string, string>? Tags { get; set; }
}

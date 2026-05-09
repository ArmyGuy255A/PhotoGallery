using PhotoGallery.Models;

namespace PhotoGallery.Interfaces;

/// <summary>
/// Provider-abstraction interface for sending transactional emails (verification codes,
/// receipts, notifications, etc.). Implementations must treat email delivery as best-effort
/// and never block or fail user-facing actions.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends the given <paramref name="message"/> via the configured email provider.
    /// Implementations should log errors rather than propagate them, as email delivery
    /// is non-critical to the originating user request.
    /// </summary>
    /// <param name="message">The fully-populated email message to send.</param>
    /// <param name="cancellationToken">Token used to cancel the underlying send operation.</param>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

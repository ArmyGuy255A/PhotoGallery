using System.Collections.Concurrent;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Services.Email;

/// <summary>
/// In-memory <see cref="IEmailService"/> implementation used for local development
/// and automated tests. Captures every sent <see cref="EmailMessage"/> in a thread-safe
/// queue that can be inspected via <see cref="SentMessages"/>.
/// </summary>
public class MockEmailService : IEmailService
{
    private readonly ConcurrentQueue<EmailMessage> _sentMessages = new();
    private readonly ILogger<MockEmailService> _logger;

    public MockEmailService(ILogger<MockEmailService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Snapshot of all messages sent through this instance, in send order.
    /// Mutating the underlying queue after the snapshot is taken does not affect
    /// the returned collection.
    /// </summary>
    public IReadOnlyCollection<EmailMessage> SentMessages => _sentMessages.ToArray();

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _sentMessages.Enqueue(message);
        _logger.LogInformation(
            "MockEmailService captured email To={To} Subject={Subject} Tags={TagCount}",
            message.To,
            message.Subject,
            message.Tags?.Count ?? 0);

        return Task.CompletedTask;
    }
}

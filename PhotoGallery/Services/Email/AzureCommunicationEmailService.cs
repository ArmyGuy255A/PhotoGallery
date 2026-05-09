using Azure;
using Azure.Communication.Email;
using PhotoGallery.Interfaces;
using PhotoGallery.Models;

namespace PhotoGallery.Services.Email;

/// <summary>
/// <see cref="IEmailService"/> backed by Azure Communication Services Email.
///
/// Configuration keys (all under the <c>Email</c> section):
/// - <c>AzureCommunicationServices:ConnectionString</c> — ACS resource connection string
/// - <c>FromAddress</c> — verified sender address (MailFrom)
/// - <c>FromDisplayName</c> — optional display name for the From header
///
/// Send failures are logged but never thrown — email is best-effort.
/// </summary>
public class AzureCommunicationEmailService : IEmailService
{
    private readonly ILogger<AzureCommunicationEmailService> _logger;
    private readonly string? _connectionString;
    private readonly string _fromAddress;
    private readonly string? _fromDisplayName;
    private readonly Lazy<EmailClient> _client;

    public AzureCommunicationEmailService(
        IConfiguration configuration,
        ILogger<AzureCommunicationEmailService> logger)
    {
        _logger = logger;
        _connectionString = configuration["Email:AzureCommunicationServices:ConnectionString"];
        _fromAddress = configuration["Email:FromAddress"] ?? string.Empty;
        _fromDisplayName = configuration["Email:FromDisplayName"];

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogWarning(
                "AzureCommunicationEmailService is registered but Email:AzureCommunicationServices:ConnectionString is not configured. " +
                "SendAsync will fail until the connection string is provided.");
        }

        _client = new Lazy<EmailClient>(() =>
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                throw new InvalidOperationException(
                    "Email:AzureCommunicationServices:ConnectionString is not configured.");
            }
            return new EmailClient(_connectionString);
        });
    }

    /// <inheritdoc />
    public async Task SendAsync(PhotoGallery.Models.EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var content = new EmailContent(message.Subject)
            {
                Html = message.HtmlBody,
                PlainText = message.TextBody ?? string.Empty,
            };

            var recipients = new EmailRecipients(new[] { new EmailAddress(message.To) });

            // ACS senderAddress is the MailFrom; display name is conveyed via the header
            // when supplied, but for the SDK constructor we simply pass the address.
            var sender = _fromAddress;

            var acsMessage = new global::Azure.Communication.Email.EmailMessage(
                senderAddress: sender,
                recipients: recipients,
                content: content);

            if (message.Tags is { Count: > 0 })
            {
                foreach (var tag in message.Tags)
                {
                    acsMessage.Headers.Add(tag.Key, tag.Value);
                }
            }

            // WaitUntil.Started: kick off the send and return immediately. Email is best-effort
            // and we don't want to block the originating user request on ACS round-trips.
            EmailSendOperation operation = await _client.Value.SendAsync(
                WaitUntil.Started,
                acsMessage,
                cancellationToken);

            _logger.LogInformation(
                "Azure Communication Services email send started. To={To} Subject={Subject} OperationId={OperationId} DisplayName={DisplayName}",
                message.To,
                message.Subject,
                operation.Id,
                _fromDisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send email via Azure Communication Services. To={To} Subject={Subject}",
                message.To,
                message.Subject);
        }
    }
}

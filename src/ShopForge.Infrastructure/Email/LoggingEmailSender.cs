using Microsoft.Extensions.Logging;
using ShopForge.Shared.Email;

namespace ShopForge.Infrastructure.Email;

// Until a delivery provider is configured (Stage 13), messages go to the log: development and tests read them there.
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation("E-mail to {Recipient}: {Subject}\n{Body}", message.To, message.Subject, message.Body);

        return Task.CompletedTask;
    }
}

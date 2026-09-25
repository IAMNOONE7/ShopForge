using Microsoft.Extensions.Logging;
using ShopForge.Shared.Email;
using ShopForge.Shared.Messaging;

namespace ShopForge.Infrastructure.Email;

// What actually puts a message on the wire. Until a provider is configured (Stage 13) it goes to the log, which is
// what development and tests read.
internal interface IEmailDelivery
{
    Task DeliverAsync(EmailMessage message, CancellationToken cancellationToken);
}

internal sealed class LoggingEmailDelivery(ILogger<LoggingEmailDelivery> logger) : IEmailDelivery
{
    public Task DeliverAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation("E-mail to {Recipient}: {Subject}\n{Body}", message.To, message.Subject, message.Body);

        return Task.CompletedTask;
    }
}

// Sending is a promise kept later: the message is written with the change that caused it and delivered by the worker,
// with retries, instead of being lost when the request fails afterwards (D-067).
internal sealed class OutboxEmailSender(IOutbox outbox) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        outbox.Enqueue(new EmailRequested(message.To, message.Subject, message.Body));

        return Task.CompletedTask;
    }

    public Task SendOutsideStoreAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        outbox.EnqueueOutsideStore(new EmailRequested(message.To, message.Subject, message.Body));

        return Task.CompletedTask;
    }
}

internal sealed class EmailRequestedHandler(IEmailDelivery delivery) : IEventHandler<EmailRequested>
{
    public Task HandleAsync(EmailRequested domainEvent, CancellationToken cancellationToken) =>
        delivery.DeliverAsync(new EmailMessage(domainEvent.To, domainEvent.Subject, domainEvent.Body), cancellationToken);
}

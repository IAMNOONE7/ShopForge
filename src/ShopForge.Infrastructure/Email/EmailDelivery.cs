using Microsoft.Extensions.Logging;
using ShopForge.Shared.Email;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

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
        // A body holds reset links, invitation tokens and whatever else was written to somebody, which is not
        // something to leave lying in an ordinary log (D-119). Development turns Debug on for this one category,
        // because there the log is how a link is read.
        logger.LogInformation("E-mail to {Recipient}: {Subject}", message.To, message.Subject);
        logger.LogDebug("E-mail body for {Recipient}: {Body}", message.To, message.Body);

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

// Where a message picks up the livery of whichever store it belongs to. Handlers write words; the look is put on
// once, here, and a message that belongs to no store goes out in the platform's plain clothes (D-111, D-121).
internal sealed class EmailRequestedHandler(
    IEmailDelivery delivery,
    IStoreContext storeContext,
    ICurrentStoreSettings storeSettings,
    EmailOptions options) : IEventHandler<EmailRequested>
{
    public async Task HandleAsync(EmailRequested domainEvent, CancellationToken cancellationToken)
    {
        var settings = storeContext.StoreId is null ? null : await storeSettings.GetAsync(cancellationToken);
        var senderName = settings?.Name ?? options.SenderName;

        await delivery.DeliverAsync(
            new EmailMessage(
                domainEvent.To,
                domainEvent.Subject,
                domainEvent.Body,
                EmailLayout.Render(Title(domainEvent.Subject, senderName), domainEvent.Body, senderName, settings?.Branding)),
            cancellationToken);
    }

    // Subjects are already written as "Store: what happened", and repeating the store's name under its own logo
    // reads badly, so the heading is what is left once the name is taken off.
    private static string Title(string subject, string senderName) =>
        subject.StartsWith($"{senderName}: ", StringComparison.Ordinal) ? subject[(senderName.Length + 2)..] : subject;
}

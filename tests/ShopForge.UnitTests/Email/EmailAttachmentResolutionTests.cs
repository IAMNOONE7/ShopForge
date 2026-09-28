using Microsoft.Extensions.Logging.Abstractions;
using ShopForge.Infrastructure.Email;
using ShopForge.Shared.Email;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.UnitTests.Email;

public sealed class EmailAttachmentResolutionTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_named_document_is_fetched_when_the_message_is_finally_sent()
    {
        var delivery = new RecordingDelivery();
        var handler = HandlerFor(delivery, new StubAttachments("order-invoice:2026-00021"));

        await handler.HandleAsync(
            new EmailRequested("buyer@example.test", "Payment received", "Thank you.", "order-invoice:2026-00021"),
            CancellationToken);

        Assert.Equal("2026-00021.pdf", delivery.Attachments.Single().FileName);
    }

    // The words are what the customer is waiting for; a document nobody can find must not hold them up (D-122).
    [Fact]
    public async Task A_document_nobody_can_find_does_not_stop_the_message()
    {
        var delivery = new RecordingDelivery();
        var handler = HandlerFor(delivery, new StubAttachments("something-else"));

        await handler.HandleAsync(
            new EmailRequested("buyer@example.test", "Payment received", "Thank you.", "order-invoice:missing"),
            CancellationToken);

        Assert.Equal("buyer@example.test", delivery.Message!.To);
        Assert.Empty(delivery.Attachments);
    }

    [Fact]
    public async Task A_message_that_names_no_document_asks_nobody()
    {
        var delivery = new RecordingDelivery();
        var source = new StubAttachments("order-invoice:2026-00021");
        var handler = HandlerFor(delivery, source);

        await handler.HandleAsync(new EmailRequested("buyer@example.test", "Your order", "Thank you."), CancellationToken);

        Assert.Empty(delivery.Attachments);
        Assert.Equal(0, source.Asked);
    }

    private static EmailRequestedHandler HandlerFor(RecordingDelivery delivery, StubAttachments source) =>
        new(delivery,
            new NoStore(),
            new UnusedStoreSettings(),
            [source],
            new EmailOptions { SenderName = "ShopForge" },
            NullLogger<EmailRequestedHandler>.Instance);

    private sealed class RecordingDelivery : IEmailDelivery
    {
        public EmailMessage? Message { get; private set; }

        public IReadOnlyList<EmailAttachment> Attachments { get; private set; } = [];

        public Task DeliverAsync(EmailMessage message, IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken)
        {
            Message = message;
            Attachments = attachments;

            return Task.CompletedTask;
        }
    }

    private sealed class StubAttachments(string known) : IEmailAttachments
    {
        public int Asked { get; private set; }

        public Task<EmailAttachment?> FindAsync(string reference, CancellationToken cancellationToken)
        {
            Asked++;

            return Task.FromResult(reference == known
                ? new EmailAttachment($"{reference.Split(':')[1]}.pdf", "application/pdf", [1, 2, 3])
                : null);
        }
    }

    private sealed class NoStore : IStoreContext
    {
        public Guid? StoreId => null;

        public Guid? TenantId => null;
    }

    private sealed class UnusedStoreSettings : ICurrentStoreSettings
    {
        public Task<StoreSettings> GetAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No store is in scope.");
    }
}

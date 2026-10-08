using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ShopForge.Infrastructure.Email;
using ShopForge.Shared.Email;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.UnitTests.Email;

public sealed class MailgunEmailDeliveryTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_shop_s_mail_is_sent_as_the_shop()
    {
        var calls = new RecordingHandler(HttpStatusCode.OK);
        var delivery = DeliveryFor(calls, storeId: Guid.CreateVersion7(), storeName: "Wooden Home");

        await delivery.DeliverAsync(new EmailMessage("buyer@example.test", "Your order", "Thank you."), [], CancellationToken);

        Assert.Equal("https://api.eu.mailgun.net/v3/mg.shopforge.test/messages", calls.Url);
        Assert.Equal("Basic YXBpOmtleS0xMjM=", calls.Authorization);
        Assert.Equal("\"Wooden Home\" <no-reply@mg.shopforge.test>", calls.Form["from"]);
        Assert.Equal("buyer@example.test", calls.Form["to"]);
        Assert.Equal("Your order", calls.Form["subject"]);
        Assert.Equal("Thank you.", calls.Form["text"]);
    }

    // An invitation or an operator's reset belongs to no shop, so it goes out as the platform (D-111).
    [Fact]
    public async Task Mail_that_belongs_to_no_store_is_sent_as_the_platform()
    {
        var calls = new RecordingHandler(HttpStatusCode.OK);
        var delivery = DeliveryFor(calls, storeId: null, storeName: "Never asked for");

        await delivery.DeliverAsync(new EmailMessage("colleague@example.test", "You are invited", "Join."), [], CancellationToken);

        Assert.Equal("\"ShopForge\" <no-reply@mg.shopforge.test>", calls.Form["from"]);
    }

    // Throwing is the whole contract with the outbox: it backs off and eventually dead-letters (D-068, D-120).
    [Fact]
    public async Task A_provider_that_refuses_the_message_fails_loudly_enough_to_be_retried()
    {
        var calls = new RecordingHandler(HttpStatusCode.InternalServerError, "the mail server is unwell");
        var delivery = DeliveryFor(calls, storeId: null, storeName: "Wooden Home");

        var problem = await Assert.ThrowsAsync<InvalidOperationException>(
            () => delivery.DeliverAsync(new EmailMessage("buyer@example.test", "Your order", "Thank you."), [], CancellationToken));

        Assert.Contains("500", problem.Message, StringComparison.Ordinal);
        Assert.Contains("unwell", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_quote_in_a_store_s_name_cannot_break_the_sender_out_of_its_own_field()
    {
        var calls = new RecordingHandler(HttpStatusCode.OK);
        var delivery = DeliveryFor(calls, storeId: Guid.CreateVersion7(), storeName: "The \"Best\" Shop <evil@example.test>");

        await delivery.DeliverAsync(new EmailMessage("buyer@example.test", "Hello", "Hello."), [], CancellationToken);

        Assert.Equal("\"The Best Shop <evil@example.test>\" <no-reply@mg.shopforge.test>", calls.Form["from"]);
    }

    [Fact]
    public async Task A_document_travels_as_a_file_beside_the_message()
    {
        var calls = new RecordingHandler(HttpStatusCode.OK);
        var delivery = DeliveryFor(calls, storeId: Guid.CreateVersion7(), storeName: "Wooden Home");

        await delivery.DeliverAsync(
            new EmailMessage("buyer@example.test", "Payment received", "Thank you."),
            [new EmailAttachment("INV-2026-00010.pdf", "application/pdf", [1, 2, 3, 4, 5])],
            CancellationToken);

        Assert.Equal("Thank you.", calls.Form["text"]);
        Assert.Equal("INV-2026-00010.pdf:application/pdf:5", calls.Files.Single());
    }

    private static MailgunEmailDelivery DeliveryFor(RecordingHandler handler, Guid? storeId, string storeName)
    {
        var options = new EmailOptions
        {
            Provider = EmailOptions.MailgunProvider,
            SenderAddress = "no-reply@mg.shopforge.test",
            Mailgun = new MailgunOptions { ApiKey = "key-123", Domain = "mg.shopforge.test" },
        };

        var httpClient = new HttpClient(handler);
        MailgunEmailDelivery.Configure(httpClient, options);

        return new MailgunEmailDelivery(
            httpClient,
            options,
            new FixedStoreContext(storeId),
            new FixedStoreSettings(storeName),
            NullLogger<MailgunEmailDelivery>.Instance);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body = "") : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public string? Authorization { get; private set; }

        public Dictionary<string, string> Form { get; } = [];

        public List<string> Files { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();

            foreach (var part in (MultipartFormDataContent)request.Content!)
            {
                var disposition = part.Headers.ContentDisposition!;
                var name = disposition.Name!.Trim('"');

                if (disposition.FileName is { } fileName)
                {
                    Files.Add($"{fileName.Trim('"')}:{part.Headers.ContentType}:{(await part.ReadAsByteArrayAsync(cancellationToken)).Length}");
                }
                else
                {
                    Form[name] = await part.ReadAsStringAsync(cancellationToken);
                }
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) };
        }
    }

    private sealed class FixedStoreContext(Guid? storeId) : IStoreContext
    {
        public Guid? StoreId => storeId;

        public Guid? TenantId => storeId is null ? null : Guid.CreateVersion7();
    }

    private sealed class FixedStoreSettings(string name) : ICurrentStoreSettings
    {
        public Task<StoreSettings> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new StoreSettings(name, Currency.Of("EUR"), "en-IE", 14, null, new StoreBranding("#112233", null), StoreSeo.None));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShopForge.Infrastructure.Email;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.Shared.Email;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;
using ShopForge.TestRig;
using ShopForge.TestRig.Comgate;
using ShopForge.TestRig.Mail;

namespace ShopForge.IntegrationTests.Rig;

// The rig's stand-ins are only worth having if ShopForge's own clients read them exactly as they read Comgate and
// Mailgun. These run the real clients against the rig in this process, so the two cannot drift apart without a test
// saying so (D-203). Nothing here needs the stack.
public sealed class RigContractTests : IDisposable
{
    private const string Merchant = "RUN-CONTRACT";
    private const string Secret = "contract-secret";

    private readonly WebApplicationFactory<ShopForge.TestRig.Rig> _rig = new WebApplicationFactory<ShopForge.TestRig.Rig>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment(RigOptions.EnvironmentName);
        builder.UseSetting("Rig:PublicOrigin", "http://rig.test");

        // Pushes go nowhere: the contract is what the client reads, not what the API does with a push.
        builder.UseSetting("Rig:ApiOrigin", "http://127.0.0.1:1");
        builder.UseSetting("ConnectionStrings:Readings", "Host=127.0.0.1;Port=1;Database=none");
    });

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private FakeComgate Comgate => _rig.Services.GetRequiredService<FakeComgate>();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public async Task A_payment_is_created_and_read_back_as_Comgate_answers_it()
    {
        Comgate.Register(Merchant, Secret);
        var client = ComgateClient();

        var created = await client.CreateAsync(new ComgateMerchant(Merchant, Secret), Payment(), CancellationToken);
        var found = await client.FindAsync(new ComgateMerchant(Merchant, Secret), created.TransactionId, CancellationToken);

        Assert.StartsWith("http://rig.test/comgate/pay/", created.RedirectUrl, StringComparison.Ordinal);
        Assert.Equal(new ComgateTransaction(created.TransactionId, "PENDING", 39000, "CZK", "2026-00001", Test: true), found);
    }

    // Any word the run sets is the word the client reads, Comgate's or not.
    [Theory]
    [InlineData("PAID")]
    [InlineData("CANCELLED")]
    [InlineData("AUTHORIZED")]
    [InlineData("REVERSAL_PENDING")]
    public async Task The_status_a_run_sets_is_the_status_the_client_reads(string status)
    {
        Comgate.Register(Merchant, Secret);
        var client = ComgateClient();
        var created = await client.CreateAsync(new ComgateMerchant(Merchant, Secret), Payment(), CancellationToken);

        Comgate.Transaction(created.TransactionId)!.Status = status;
        var found = await client.FindAsync(new ComgateMerchant(Merchant, Secret), created.TransactionId, CancellationToken);

        Assert.Equal(status, found!.Status);
    }

    // The shopper's button on the page becomes the status, and the browser is sent back where ShopForge asked.
    [Fact]
    public async Task Paying_on_the_page_sends_the_shopper_back_and_the_client_reads_paid()
    {
        Comgate.Register(Merchant, Secret);
        var client = ComgateClient();
        var created = await client.CreateAsync(new ComgateMerchant(Merchant, Secret), Payment(), CancellationToken);
        using var browser = _rig.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var page = await browser.GetAsync(new Uri(created.RedirectUrl).PathAndQuery, CancellationToken);
        using var paid = await browser.PostAsync(
            new Uri(created.RedirectUrl).PathAndQuery, new FormUrlEncodedContent([new KeyValuePair<string, string>("outcome", "paid")]), CancellationToken);
        var found = await client.FindAsync(new ComgateMerchant(Merchant, Secret), created.TransactionId, CancellationToken);

        Assert.Contains("Pay", await page.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, paid.StatusCode);
        Assert.Equal("http://shop.test/order/2026-00001?token=abc", paid.Headers.Location!.ToString());
        Assert.Equal("PAID", found!.Status);
    }

    [Fact]
    public async Task A_refund_is_taken_refused_or_failed_as_the_run_says()
    {
        Comgate.Register(Merchant, Secret);
        var client = ComgateClient();
        var merchant = new ComgateMerchant(Merchant, Secret);
        var created = await client.CreateAsync(merchant, Payment(), CancellationToken);

        await client.RefundAsync(merchant, created.TransactionId, 10000, test: true, CancellationToken);
        Comgate.Merchant(Merchant)!.Behaviour = new GatewayBehaviour(Refund: GatewayBehaviour.Refuse);
        var refused = await Assert.ThrowsAsync<RefundRefusedException>(() => client.RefundAsync(merchant, created.TransactionId, 10000, test: true, CancellationToken));
        Comgate.Merchant(Merchant)!.Behaviour = new GatewayBehaviour(Refund: GatewayBehaviour.Fail);
        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => client.RefundAsync(merchant, created.TransactionId, 10000, test: true, CancellationToken));

        Assert.Equal(10000, Comgate.Transaction(created.TransactionId)!.Refunded);
        Assert.Contains("1401", refused.Message, StringComparison.Ordinal);
        Assert.Contains("500", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_test_merchant_s_statement_days_are_empty_and_an_unknown_payment_is_none()
    {
        Comgate.Register(Merchant, Secret);
        var client = ComgateClient();
        var merchant = new ComgateMerchant(Merchant, Secret);

        Assert.Empty(await client.TransferIdsAsync(merchant, new DateOnly(2026, 10, 15), CancellationToken));
        Assert.Empty(await client.TransferLinesAsync(merchant, 1, CancellationToken));
        Assert.Null(await client.FindAsync(merchant, "NONE-SUCH-PAYM", CancellationToken));
    }

    [Fact]
    public async Task A_wrong_secret_is_refused_as_Comgate_refuses_it()
    {
        Comgate.Register(Merchant, Secret);

        var refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => ComgateClient().CreateAsync(new ComgateMerchant(Merchant, "not-the-secret"), Payment(), CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    // The real Mailgun delivery, from the form it builds to the link a journey follows — without the full stop that
    // ends the sentence the link is in.
    [Fact]
    public async Task A_letter_sent_through_ShopForge_s_Mailgun_delivery_is_kept_with_its_links()
    {
        var options = new EmailOptions
        {
            Provider = EmailOptions.MailgunProvider,
            SenderAddress = "shop@e2e.test",
            Mailgun = new MailgunOptions { ApiKey = "e2e", Domain = "e2e.test", BaseUrl = "http://localhost/mailgun" },
        };
        using var http = _rig.CreateClient();
        MailgunEmailDelivery.Configure(http, options);
        var delivery = new MailgunEmailDelivery(
            http, new PlatformSender(options), options, new StoreContext(), new NoStore(), NullLogger<MailgunEmailDelivery>.Instance);

        await delivery.DeliverAsync(
            new EmailMessage("owner@rcontract.e2e.test", "You are invited", "Choose a password to accept: http://localhost/invitations/accept?token=a%2Bb. It expires."),
            [],
            CancellationToken);
        var kept = Assert.Single(_rig.Services.GetRequiredService<FakeMailgun>().To("rcontract.e2e.test", null));

        Assert.Equal(("You are invited", "owner@rcontract.e2e.test"), (kept.Subject, kept.To));
        Assert.Equal(["http://localhost/invitations/accept?token=a%2Bb"], kept.Links);
    }

    // The rig refuses to start anywhere but beside an end-to-end API.
    [Fact]
    public void The_rig_will_not_start_outside_the_end_to_end_environment()
    {
        using var development = _rig.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

        var refused = Assert.ThrowsAny<Exception>(() => development.Services);

        Assert.Contains("only in the E2E environment", Innermost(refused).Message, StringComparison.Ordinal);
    }

    // An API that answers but is not the end-to-end one — a development API, say — is asked only whether it is one, and
    // nothing is written to it.
    [Fact]
    public async Task A_run_is_not_started_through_an_api_that_is_not_the_end_to_end_one()
    {
        using var development = new ApiOnThisMachine("""{"environment":"Development","database":"shopforge"}""");
        using var rig = _rig.WithWebHostBuilder(builder => builder.UseSetting("Rig:ApiOrigin", development.Origin));
        using var client = rig.CreateClient();

        using var refused = await client.PostAsync("/runs", null, CancellationToken);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
        Assert.Contains("is not the end-to-end API", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(["GET /api/e2e"], development.Asked);
    }

    [Fact]
    public async Task A_run_is_not_started_when_the_api_cannot_be_reached()
    {
        using var client = _rig.CreateClient();

        using var refused = await client.PostAsync("/runs", null, CancellationToken);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
        Assert.Contains("is not an end-to-end API", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    private static Exception Innermost(Exception exception) => exception.InnerException is { } inner ? Innermost(inner) : exception;

    private ComgateHttpPayments ComgateClient()
    {
        var http = _rig.CreateClient();
        http.BaseAddress = new Uri(http.BaseAddress!, "comgate/");

        return new ComgateHttpPayments(http);
    }

    private static ComgatePayment Payment() =>
        new(
            PriceInMinorUnits: 39000,
            Currency: "CZK",
            Label: "Order 2026-00001",
            ReferenceId: "2026-00001",
            Email: "buyer@rcontract.e2e.test",
            FullName: "Eva Kupující",
            Phone: null,
            Country: "CZ",
            Language: "cs",
            Delivery: "PICKUP",
            Category: "PHYSICAL",
            Test: true,
            ExpirationTime: "25m",
            ReturnUrl: "http://shop.test/order/2026-00001?token=abc",
            CancelUrl: "http://shop.test/cart",
            PendingUrl: "http://shop.test/order/2026-00001?token=abc");

    // Mail with no store in scope reads nothing about one.
    // The platform's own domain and key, as every message with no store of its own goes out (D-211).
    private sealed class PlatformSender(EmailOptions options) : IMailSenders
    {
        public Task<MailSender> CurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MailSender(options.Mailgun.Domain, options.Mailgun.ApiKey, options.SenderAddress, ReplyTo: null));
    }

    private sealed class NoStore : ICurrentStoreSettings
    {
        public Task<StoreSettings> GetAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No store is in scope.");
    }

    // An API on this machine that answers its marker with whatever it is given and notes everything it is asked.
    private sealed class ApiOnThisMachine : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly string _marker;
        private readonly List<string> _asked = [];

        public ApiOnThisMachine(string marker)
        {
            _marker = marker;
            var free = new TcpListener(IPAddress.Loopback, 0);
            free.Start();
            var port = ((IPEndPoint)free.LocalEndpoint).Port;
            free.Stop();

            Origin = $"http://localhost:{port}/";
            _listener.Prefixes.Add(Origin);
            _listener.Start();
            _ = AnswerAsync();
        }

        public string Origin { get; }

        public List<string> Asked
        {
            get
            {
                lock (_asked)
                {
                    return [.. _asked];
                }
            }
        }

        public void Dispose() => _listener.Close();

        private async Task AnswerAsync()
        {
            while (_listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();

                    lock (_asked)
                    {
                        _asked.Add($"{context.Request.HttpMethod} {context.Request.Url!.AbsolutePath}");
                    }

                    var body = Encoding.UTF8.GetBytes(context.Request.Url!.AbsolutePath == "/api/e2e" ? _marker : "{}");
                    context.Response.ContentType = "application/json";
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    await context.Response.OutputStream.WriteAsync(body);
                    context.Response.Close();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }
            }
        }
    }
}

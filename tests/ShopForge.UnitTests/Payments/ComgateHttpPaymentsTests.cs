using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShopForge.Infrastructure.Payments.Comgate;

namespace ShopForge.UnitTests.Payments;

// What actually goes over the wire to Comgate, driven through the real client rather than a stand-in. Every
// element name here comes from the brief and has never been submitted to Comgate, so this is the one place
// that says what ShopForge sends (D-177).
public sealed class ComgateHttpPaymentsTests
{
    private static readonly ComgateMerchant Merchant = new("MERCHANT-1", "the-secret");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_refund_names_the_transaction_the_amount_and_the_currency()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"code":0,"message":"OK"}""");
        var payments = Client(handler);

        await payments.RefundAsync(Merchant, "trans-1", 64900, "CZK", CancellationToken);

        Assert.Equal("https://pay.comgate.test/v2.0/refund.json", handler.Url);
        Assert.Equal("POST", handler.Method);
        Assert.Equal("trans-1", (string?)handler.Body!["transId"]);
        Assert.Equal(64900, (long?)handler.Body["amount"]);
        Assert.Equal("CZK", (string?)handler.Body["curr"]);
    }

    // The merchant and its secret go in the header and nowhere else: not in the path, not in the body, and so
    // not into anything that logs a request (18c, D-139).
    [Fact]
    public async Task A_refund_authenticates_in_the_header_and_carries_no_secret_anywhere_else()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var payments = Client(handler);

        await payments.RefundAsync(Merchant, "trans-1", 100, "CZK", CancellationToken);

        Assert.StartsWith("Basic ", handler.Authorization, StringComparison.Ordinal);
        Assert.DoesNotContain("the-secret", handler.Url!, StringComparison.Ordinal);
        Assert.DoesNotContain("the-secret", handler.Body!.ToJsonString(), StringComparison.Ordinal);
    }

    // A refund that did not happen must not look like one that did: the returns flow has already written down
    // what it owes, and a quiet success there is money the shop thinks it has sent.
    [Fact]
    public async Task A_refund_comgate_refuses_fails_rather_than_passing_quietly()
    {
        var payments = Client(new RecordingHandler(HttpStatusCode.BadRequest, """{"code":1309,"message":"Refund is higher than the payment."}"""));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => payments.RefundAsync(Merchant, "trans-1", 999_999, "CZK", CancellationToken));
    }

    [Fact]
    public async Task A_transaction_comgate_does_not_have_reads_as_nothing_rather_than_failing()
    {
        var payments = Client(new RecordingHandler(HttpStatusCode.NotFound));

        Assert.Null(await payments.FindAsync(Merchant, "trans-gone", CancellationToken));
    }

    private static IComgatePayments Client(RecordingHandler handler) =>
        new ComgateHttpPayments(new HttpClient(handler) { BaseAddress = new Uri("https://pay.comgate.test/") });

    private sealed class RecordingHandler(HttpStatusCode status, string body = "{}") : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public string? Method { get; private set; }

        public string? Authorization { get; private set; }

        public JsonNode? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            Method = request.Method.Method;
            Authorization = request.Headers.Authorization?.ToString();

            if (request.Content is { } content)
            {
                Body = JsonNode.Parse(await content.ReadAsStringAsync(cancellationToken));
            }

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}

using System.Net;
using System.Text.Json.Nodes;
using ShopForge.Infrastructure.Payments.Comgate;
using ShopForge.Shared.Payments;

namespace ShopForge.UnitTests.Payments;

// What actually goes over the wire to Comgate, driven through the real client rather than a stand-in. The element
// names were checked against Comgate's Merchant API reference on 2026-10-13 (D-195); this pins what ShopForge
// sends and how it reads the answers, not that Comgate accepts them — only a sandbox round trip can say that.
public sealed class ComgateHttpPaymentsTests
{
    private static readonly ComgateMerchant Merchant = new("MERCHANT-1", "the-secret");

    private static readonly ComgatePayment Payment = new(
        64900,
        "CZK",
        "Order 2026-1",
        "2026-1",
        "buyer@example.test",
        "Alex Buyer",
        "+420123456789",
        "CZ",
        "cs",
        "HOME_DELIVERY",
        "PHYSICAL_GOODS_ONLY",
        true,
        "30m",
        "https://shop.test/order/2026-1?token=t",
        "https://shop.test/cart",
        "https://shop.test/order/2026-1?token=t");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Flat payer fields, the method the shopper chooses on Comgate's page, and the three return addresses under
    // the names Comgate gives them. The brief this was first written from had a nested payer and other names.
    [Fact]
    public async Task A_payment_is_created_with_the_fields_comgate_documents()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.Created, """{"code":0,"message":"OK","transId":"AB12-CD34-EF56","redirect":"https://payments.comgate.cz/client/instructions/index?id=AB12-CD34-EF56"}""");

        var created = await Client(handler).CreateAsync(Merchant, Payment, CancellationToken);
        var body = handler.Body!.AsObject();

        Assert.Equal(("https://pay.comgate.test/v2.0/payment.json", "POST"), (handler.Url, handler.Method));
        Assert.Equal(new ComgateCreated("AB12-CD34-EF56", "https://payments.comgate.cz/client/instructions/index?id=AB12-CD34-EF56"), created);
        Assert.Equal(
            ["category", "country", "curr", "delivery", "email", "expirationTime", "fullName", "label", "lang", "method", "phone", "price", "refId", "test", "url_cancelled", "url_paid", "url_pending"],
            body.Select(field => field.Key).Order(StringComparer.Ordinal));
        Assert.Equal(("ALL", "Alex Buyer", "buyer@example.test"), ((string?)body["method"], (string?)body["fullName"], (string?)body["email"]));
        Assert.Equal((64900L, "CZK", true, "30m"), ((long?)body["price"], (string?)body["curr"], (bool?)body["test"], (string?)body["expirationTime"]));
        Assert.Equal(("CZ", "PHYSICAL_GOODS_ONLY"), ((string?)body["country"], (string?)body["category"]));
        Assert.Equal("https://shop.test/cart", (string?)body["url_cancelled"]);
    }

    [Fact]
    public async Task A_payment_with_no_telephone_number_sends_none()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, """{"code":0,"message":"OK","transId":"T","redirect":"https://pay.test/T"}""");

        await Client(handler).CreateAsync(Merchant, Payment with { Phone = null }, CancellationToken);

        Assert.False(handler.Body!.AsObject().ContainsKey("phone"));
    }

    // Comgate's own code is the answer; a status line that reads as success with a code that does not is not a
    // payment, and a shopper must not be sent anywhere on the strength of it.
    [Fact]
    public async Task A_payment_comgate_did_not_create_is_an_error_whatever_the_status_line_says()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.Created, """{"code":1309,"message":"incorrect amount","transId":"AB12","redirect":"https://pay.test/AB12"}""");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).CreateAsync(Merchant, Payment, CancellationToken));

        Assert.Contains("1309", failure.Message, StringComparison.Ordinal);
    }

    // The status answer writes `test` and the price as text, unlike the request that created the payment. Read
    // as a boolean, `test` failed every lookup there would ever have been.
    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public async Task A_status_answer_is_read_the_way_comgate_writes_it(string test, bool expected)
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            $$"""{"code":0,"message":"OK","test":"{{test}}","price":"10000","curr":"CZK","label":"Order 2026-1","refId":"2026-1","email":"buyer@example.test","transId":"AB12-CD34-EF56","status":"PAID"}""");

        var found = await Client(handler).FindAsync(Merchant, "AB12-CD34-EF56", CancellationToken);

        Assert.Equal(new ComgateTransaction("AB12-CD34-EF56", "PAID", 10000, "CZK", "2026-1", expected), found);
        Assert.Equal(("https://pay.comgate.test/v2.0/payment/transId/AB12-CD34-EF56.json", "GET"), (handler.Url, handler.Method));
    }

    [Fact]
    public async Task A_status_answer_carrying_an_error_code_is_not_a_transaction()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK, """{"code":1400,"message":"bad request","transId":"AB12-CD34-EF56","status":"PAID","price":"10000","curr":"CZK","refId":"2026-1","test":"false"}""");

        Assert.Null(await Client(handler).FindAsync(Merchant, "AB12-CD34-EF56", CancellationToken));
    }

    [Fact]
    public async Task A_transaction_comgate_does_not_have_reads_as_nothing_rather_than_failing()
    {
        var payments = Client(new RecordingHandler(HttpStatusCode.NotFound));

        Assert.Null(await payments.FindAsync(Merchant, "trans-gone", CancellationToken));
    }

    // A refund names the transaction, how much, and whether it is a test. No currency: Comgate gives money back in
    // the transaction's own.
    [Fact]
    public async Task A_refund_names_the_transaction_the_amount_and_whether_it_is_a_test()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"code":0,"message":"OK"}""");

        await Client(handler).RefundAsync(Merchant, "trans-1", 64900, true, CancellationToken);

        Assert.Equal(("https://pay.comgate.test/v2.0/refund.json", "POST"), (handler.Url, handler.Method));
        Assert.Equal(["amount", "test", "transId"], handler.Body!.AsObject().Select(field => field.Key).Order(StringComparer.Ordinal));
        Assert.Equal(("trans-1", 64900L, true), ((string?)handler.Body["transId"], (long?)handler.Body["amount"], (bool?)handler.Body["test"]));
    }

    // The merchant and its secret go in the header and nowhere else: not in the path, not in the body, and so
    // not into anything that logs a request (18c, D-139).
    [Fact]
    public async Task A_refund_authenticates_in_the_header_and_carries_no_secret_anywhere_else()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"code":0,"message":"OK"}""");

        await Client(handler).RefundAsync(Merchant, "trans-1", 100, false, CancellationToken);

        Assert.StartsWith("Basic ", handler.Authorization, StringComparison.Ordinal);
        Assert.DoesNotContain("the-secret", handler.Url!, StringComparison.Ordinal);
        Assert.DoesNotContain("the-secret", handler.Body!.ToJsonString(), StringComparison.Ordinal);
    }

    // Comgate refuses under an HTTP 200, with a code in the body. Reading only the status line took this for a
    // refund that happened (D-194, D-195).
    [Theory]
    [InlineData(HttpStatusCode.OK, 1402)]
    [InlineData(HttpStatusCode.OK, 1401)]
    [InlineData(HttpStatusCode.BadRequest, 1400)]
    public async Task A_refund_comgate_refuses_is_a_refusal_whatever_the_status_line_says(HttpStatusCode status, int code)
    {
        var handler = new RecordingHandler(status, $$"""{"code":{{code}},"message":"refund amount higher than allowed"}""");

        var refusal = await Assert.ThrowsAsync<RefundRefusedException>(
            () => Client(handler).RefundAsync(Merchant, "trans-1", 999_999, false, CancellationToken));

        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), refusal.Message, StringComparison.Ordinal);
    }

    // An error Comgate does not say anything more about may have moved the money, so it is neither taken for a
    // refund nor for a refusal that could safely be tried again.
    [Theory]
    [InlineData(HttpStatusCode.OK, """{"code":1500,"message":"unexpected error"}""")]
    [InlineData(HttpStatusCode.InternalServerError, """{"code":1100,"message":"unknown error"}""")]
    [InlineData(HttpStatusCode.BadGateway, "<html>bad gateway</html>")]
    public async Task A_refund_whose_outcome_comgate_did_not_say_is_neither(HttpStatusCode status, string body)
    {
        var handler = new RecordingHandler(status, body);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Client(handler).RefundAsync(Merchant, "trans-1", 100, false, CancellationToken));
    }

    // A day's transfers, from the path Comgate documents and with the merchant's own credentials. The day is written
    // the way Comgate reads it, a Prague date (D-198).
    [Fact]
    public async Task A_days_transfers_are_listed_from_the_documented_path()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """[{"transferId":1234567,"transferDate":"2023-01-25","accountCounterparty":"0/0000","accountOutgoing":"123456789/0000","variableSymbol":"12345678"}]""");

        var transfers = await Client(handler).TransferIdsAsync(Merchant, new DateOnly(2023, 1, 25), CancellationToken);

        Assert.Equal([1234567L], transfers);
        Assert.Equal(("https://pay.comgate.test/v2.0/transferList/date/2023-01-25.json", "GET"), (handler.Url, handler.Method));
        Assert.StartsWith("Basic ", handler.Authorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_day_comgate_paid_nothing_out_on_is_an_empty_day()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent, string.Empty);

        Assert.Empty(await Client(handler).TransferIdsAsync(Merchant, new DateOnly(2023, 1, 25), CancellationToken));
    }

    // Comgate's own sample, character for character: Czech names, amounts as text with a decimal comma.
    [Fact]
    public async Task A_transfers_lines_are_read_under_comgates_own_names()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """
            [{"typ":1,"Merchant":"123456","Datum založení":"2023-01-06 14:11:30","Datum zaplacení":"2023-01-06 14:21:30",
              "Datum převodu":"2023-01-10","Měsíc fakturace":null,"ID Comgate":"AAAA-BBBB-CCCC","Metoda":"Card payment",
              "Produkt":null,"Popis":"description eshop payment","E-mail plátce":"name.lastname@email.cz",
              "Variabilní symbol plátce":"123456789","Variabilní symbol převodu":"123456789","ID od klienta":"1234",
              "Měna":"EUR","Potvrzená částka":"10,00","Převedená částka":"10,00","Poplatek celkem":"0,35",
              "Poplatek mezibankovní":"0,25","Poplatek asociace":"0,25","Poplatek zpracovatel":"-0,15","Typ karty":"EU_UNREGULATED"}]
            """);

        var lines = await Client(handler).TransferLinesAsync(Merchant, 1234567, CancellationToken);

        Assert.Equal(("https://pay.comgate.test/v2.0/singleTransfer/transferId/1234567.json", "GET"), (handler.Url, handler.Method));
        Assert.Equal(
            new ComgateTransferLine(1, "AAAA-BBBB-CCCC", "1234", "EUR", "10,00", "10,00", "0,35", "2023-01-06 14:21:30", "2023-01-10"),
            Assert.Single(lines));
    }

    // A day that could not be read is not a day with nothing in it: taken for one, it would never be asked for again.
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_statement_that_cannot_be_read_is_a_failure_and_not_an_empty_day(HttpStatusCode status)
    {
        var handler = new RecordingHandler(status, """{"code":1400,"message":"bad request"}""");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Client(handler).TransferIdsAsync(Merchant, new DateOnly(2023, 1, 25), CancellationToken));
    }

    private static ComgateHttpPayments Client(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://pay.comgate.test/") });

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
                Content = new StringContent(body, System.Text.Encoding.UTF8, body.StartsWith('<') ? "text/html" : "application/json"),
            };
        }
    }
}

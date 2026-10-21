using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShopForge.Infrastructure.Shipping.Packeta;

namespace ShopForge.UnitTests.Shipping;

// What goes over the wire to Packeta's point check, driven through the real client. The request and the answers are the
// ones in Packeta's widget reference as read on 2026-10-21 (D-213); this pins what ShopForge sends and how it reads the
// documented answers, not that Packeta accepts them — Packeta has no sandbox, and nothing here has been sent to it.
public sealed class PacketaHttpClientTests
{
    private const string ApiKey = "abcdef0123456789";

    private static readonly PacketaPointChoice Choice = new("4321", "cz", "zbox");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // A POST of JSON under the widget's address, with the same key and the same options the widget was opened with, and
    // sent with its length rather than in chunks. The brief this was first written from had a GET to another host, the
    // API password in its path, and the options as a query string.
    [Fact]
    public async Task A_point_is_checked_where_and_how_packeta_documents()
    {
        const string question = """{"apiKey":"abcdef0123456789","point":{"id":"4321"},"options":{"country":"cz","vendors":[{"country":"cz","group":"zbox"}]}}""";
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"isValid":true,"point":{"name":"Z-BOX"},"errors":[]}""");

        await Client(handler).ValidatePointAsync(ApiKey, Choice, CancellationToken);

        Assert.Equal(("https://widget.packeta.test/v6/pps/api/widget/v1/validate", "POST"), (handler.Url, handler.Method));
        Assert.Equal("application/json", handler.ContentType);
        Assert.Equal(question, handler.Body!.ToJsonString());
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(question), handler.ContentLength);
    }

    // Packeta's own example, accepted: the point as Packeta describes it, under the id that was asked about, which the
    // answer does not repeat. Its city carries a trailing space and its country is in lower case.
    [Fact]
    public async Task A_point_packeta_accepts_is_read_from_the_documented_answer()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """
            {"isValid":true,"point":{"name":"Z-BOX","address":{"street":"Českomoravská 2408","city":"Praha ","zip":"190 00","country":"cz"},"group":"zbox"},"errors":[]}
            """);

        var check = await Client(handler).ValidatePointAsync(ApiKey, Choice, CancellationToken);

        Assert.Equal(new PacketaPoint("4321", "Z-BOX", "Českomoravská 2408", "Praha", "190 00", "CZ"), check.Point);
        Assert.Empty(check.Refusals);
    }

    // The reference says the name is always there; if it is not, the point is still the one Packeta accepted.
    [Fact]
    public async Task A_point_accepted_without_a_description_keeps_its_id_for_a_name()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"isValid":true}""");

        var check = await Client(handler).ValidatePointAsync(ApiKey, Choice, CancellationToken);

        Assert.Equal(new PacketaPoint("4321", "4321", string.Empty, string.Empty, string.Empty, "CZ"), check.Point);
    }

    // A refusal comes under a 200, with every reason Packeta found. The brief read anything but a success as Packeta
    // not answering, which would have told a shopper with a full box to come back later.
    [Theory]
    [InlineData("""{"isValid":false,"point":{"name":"Z-BOX","address":{"street":"Českomoravská 2408","city":"Praha ","zip":"190 00","country":"cz"},"group":"zbox"},"errors":[{"code":"NoAgeVerification","description":"The PUDO point does not offer Age Verification Service."}]}""", new[] { "NoAgeVerification" })]
    [InlineData("""{"isValid":false,"point":null,"errors":[{"code":"PickupPointIsFull","description":"Full."},{"code":"InvalidVendor","description":"Not a Z-BOX."}]}""", new[] { "PickupPointIsFull", "InvalidVendor" })]
    [InlineData("""{"isValid":false,"errors":[]}""", new string[0])]
    public async Task A_point_packeta_refuses_is_a_refusal_with_its_reasons(string answer, string[] reasons)
    {
        var check = await Client(new RecordingHandler(HttpStatusCode.OK, answer)).ValidatePointAsync(ApiKey, Choice, CancellationToken);

        Assert.Null(check.Point);
        Assert.Equal(reasons, check.Refusals);
    }

    // A question Packeta could not read and a key it does not know say nothing about the point. Neither may become a
    // refusal, or a broken connection would read as a shopper's mistake.
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "could not read")]
    [InlineData(HttpStatusCode.Unauthorized, "API key")]
    public async Task Neither_a_bad_question_nor_an_unknown_key_is_a_refusal(HttpStatusCode status, string said)
    {
        var handler = new RecordingHandler(status, """{"errors":[{"code":"Unknown","description":"Something."}]}""");

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).ValidatePointAsync(ApiKey, Choice, CancellationToken));

        Assert.Equal(status, failure.StatusCode);
        Assert.Contains(said, failure.Message, StringComparison.Ordinal);
    }

    // Silence is not refusal (D-160): a failure, or an answer that does not say whether the point is valid, throws.
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "<html>down</html>", typeof(HttpRequestException))]
    [InlineData(HttpStatusCode.OK, "{}", typeof(InvalidDataException))]
    [InlineData(HttpStatusCode.OK, """{"isValid":null,"errors":[]}""", typeof(InvalidDataException))]
    [InlineData(HttpStatusCode.OK, "not json", typeof(JsonException))]
    public async Task An_answer_that_says_nothing_about_the_point_is_not_a_refusal(HttpStatusCode status, string answer, Type thrown)
    {
        var failure = await Record.ExceptionAsync(
            () => Client(new RecordingHandler(status, answer)).ValidatePointAsync(ApiKey, Choice, CancellationToken));

        Assert.IsType(thrown, failure, exactMatch: false);
    }

    // A parcel is addressed to a point by an unsigned 32-bit number, so nothing else could ever be booked.
    [Theory]
    [InlineData("4321", true)]
    [InlineData("4294967295", true)]
    [InlineData("4294967296", false)]
    [InlineData("-1", false)]
    [InlineData(" 4321", false)]
    [InlineData("4321 ", false)]
    [InlineData("Z-BOX-99", false)]
    [InlineData("", false)]
    public void Only_a_number_packeta_could_address_a_parcel_to_names_a_point(string pointId, bool couldBeBooked) =>
        Assert.Equal(couldBeBooked, (Choice with { PointId = pointId }).CouldBeBooked);

    // The id, the barcode and the barcode as printed on the label are one packet, and its page is Packeta's.
    [Theory]
    [InlineData("1234567890", "https://tracking.packeta.com/cs/?id=1234567890")]
    [InlineData("Z1234567890", "https://tracking.packeta.com/cs/?id=1234567890")]
    [InlineData("z1234567890", "https://tracking.packeta.com/cs/?id=1234567890")]
    [InlineData("Z 123 4567 890", "https://tracking.packeta.com/cs/?id=1234567890")]
    [InlineData("123456789", null)]
    [InlineData("Z12345678901", null)]
    [InlineData("ZZ1234567890", null)]
    [InlineData("DE-1234567890", null)]
    [InlineData("", null)]
    public void A_packeta_number_links_to_packetas_tracking_page(string trackingNumber, string? link) =>
        Assert.Equal(link, PacketaTracking.Url(trackingNumber, "cs"));

    private static PacketaHttpClient Client(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://widget.packeta.test/v6/") });

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public string? Method { get; private set; }

        public string? ContentType { get; private set; }

        public long? ContentLength { get; private set; }

        public JsonNode? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            Method = request.Method.Method;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            ContentLength = request.Content?.Headers.ContentLength;

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

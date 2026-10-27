using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ShopForge.Infrastructure.Shipping.Packeta;

// Everything that depends on reading Packeta's documentation correctly, in one file and nowhere else (D-155). Checked
// against docs.packeta.com on 2026-10-21, whose changelog's latest entry was 2026-10-16 (D-213). Packeta has no sandbox,
// so nothing here has been sent to it; the tests pin what ShopForge sends and how it reads the documented answers.
internal interface IPacketaClient
{
    // What Packeta says about a point chosen in its map, a refusal included. Throwing means Packeta could not be asked
    // or did not answer the question, which must not be mistaken for a refusal (D-160).
    Task<PacketaPointCheck> ValidatePointAsync(string apiKey, PacketaPointChoice choice, CancellationToken cancellationToken);
}

// What the widget was opened with, repeated to the validator as Packeta asks, so the two are asking about the same
// thing: a point that is real but belongs to another country or another kind of place is not one this method may use.
// Packeta calls a country and a group together a vendor; `zbox` is the group of its boxes. The parcel's weight, in
// kilograms, and its sides, in whole centimetres, are the widget's and the validator's own options: a point that cannot
// take a parcel that heavy or that large is neither shown nor accepted (D-220, docs.packeta.com/docs/pudo-delivery/widget).
internal sealed record PacketaPointChoice(
    string PointId,
    string Country,
    string Group,
    decimal? WeightKg = null,
    int? LengthCm = null,
    int? WidthCm = null,
    int? DepthCm = null)
{
    // Packeta's own points are numbered, and a parcel is addressed to one by `addressId`, an unsigned 32-bit number. A
    // code that is not one could never be booked, whatever the validator said, so nobody needs to be asked.
    public bool CouldBeBooked => uint.TryParse(PointId, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}

// The point when Packeta accepts it, and otherwise the codes it gave for refusing (`PickupPointIsFull`, `InvalidVendor`).
internal sealed record PacketaPointCheck(PacketaPoint? Point, IReadOnlyList<string> Refusals);

internal sealed record PacketaPoint(string Id, string Name, string Street, string City, string Zip, string Country);

internal sealed class PacketaHttpClient(HttpClient client) : IPacketaClient
{
    // Under the widget's own address, `https://widget.packeta.com/v6/`.
    public const string ValidationPath = "pps/api/widget/v1/validate";

    // The API key is the one the shopper's page opens the map with: Packeta asks the validator for the same key and the
    // same options. The API password is not sent, because this call has no use for it — Packeta's guide says never to
    // put it in the widget, and the validator is the widget's.
    public async Task<PacketaPointCheck> ValidatePointAsync(string apiKey, PacketaPointChoice choice, CancellationToken cancellationToken)
    {
        var question = new Question(
            apiKey,
            new QuestionPoint(choice.PointId),
            new QuestionOptions(
                choice.Country,
                [new QuestionVendor(choice.Country, choice.Group)],
                choice.WeightKg,
                choice.LengthCm,
                choice.WidthCm,
                choice.DepthCm));

        // Sent whole, with its length, rather than streamed in chunks: nothing in the reference says the endpoint reads a
        // chunked body, and every server reads a length.
        using var body = new StringContent(JsonSerializer.Serialize(question), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(ValidationPath, body, cancellationToken);

        // A 200 answers the question either way. A 400 is a question Packeta could not read and a 401 a key it does not
        // know; neither says anything about the point, so neither is a refusal.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            throw new HttpRequestException(
                response.StatusCode == HttpStatusCode.Unauthorized
                    ? "Packeta did not accept the store's API key."
                    : "Packeta could not read the question about the point.",
                inner: null,
                response.StatusCode);
        }

        response.EnsureSuccessStatusCode();

        var answer = await response.Content.ReadFromJsonAsync<Answer>(cancellationToken)
            ?? throw new InvalidDataException("Packeta answered the point check with nothing.");

        return answer.IsValid switch
        {
            true => new PacketaPointCheck(Point(choice, answer.Point), []),
            false => new PacketaPointCheck(null, [.. (answer.Errors ?? []).Select(error => error.Code).OfType<string>()]),
            null => throw new InvalidDataException("Packeta's answer to the point check does not say whether the point is valid."),
        };
    }

    // The answer does not repeat the id; it is the one that was asked about. Packeta writes the country in lower case and
    // has been seen to leave a space after a city, and an order keeps neither.
    private static PacketaPoint Point(PacketaPointChoice choice, AnswerPoint? point) => new(
        choice.PointId,
        Text(point?.Name) ?? choice.PointId,
        Text(point?.Address?.Street) ?? string.Empty,
        Text(point?.Address?.City) ?? string.Empty,
        Text(point?.Address?.Zip) ?? string.Empty,
        (Text(point?.Address?.Country) ?? choice.Country).ToUpperInvariant());

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Question(
        [property: JsonPropertyName("apiKey")] string ApiKey,
        [property: JsonPropertyName("point")] QuestionPoint Point,
        [property: JsonPropertyName("options")] QuestionOptions Options);

    private sealed record QuestionPoint([property: JsonPropertyName("id")] string Id);

    // A parcel nobody estimated is asked about without its weight and sides, as before (D-213).
    private sealed record QuestionOptions(
        [property: JsonPropertyName("country")] string Country,
        [property: JsonPropertyName("vendors")] IReadOnlyList<QuestionVendor> Vendors,
        [property: JsonPropertyName("weight"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Weight,
        [property: JsonPropertyName("length"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Length,
        [property: JsonPropertyName("width"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Width,
        [property: JsonPropertyName("depth"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Depth);

    private sealed record QuestionVendor(
        [property: JsonPropertyName("country")] string Country,
        [property: JsonPropertyName("group")] string Group);

    private sealed record Answer(
        [property: JsonPropertyName("isValid")] bool? IsValid,
        [property: JsonPropertyName("point")] AnswerPoint? Point,
        [property: JsonPropertyName("errors")] IReadOnlyList<AnswerError>? Errors);

    private sealed record AnswerPoint(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("address")] AnswerAddress? Address);

    private sealed record AnswerAddress(
        [property: JsonPropertyName("street")] string? Street,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("zip")] string? Zip,
        [property: JsonPropertyName("country")] string? Country);

    private sealed record AnswerError(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("description")] string? Description);
}

// Packeta's public page for one packet: `https://tracking.packeta.com/<language>/?id=<packet id>`. A packet's id is ten
// digits and its barcode is the id behind a Z, printed spaced as `Z 123 4567 890`, so whichever of the three a merchant
// copies out of Packeta's portal names the same packet. Anything else is not a Packeta number and gets no link.
internal static partial class PacketaTracking
{
    public static string? Url(string trackingNumber, string language) =>
        PacketNumber().Match(trackingNumber.Replace(" ", string.Empty, StringComparison.Ordinal)) is { Success: true } packet
            ? $"https://tracking.packeta.com/{language}/?id={packet.Groups["id"].Value}"
            : null;

    [GeneratedRegex("^[Zz]?(?<id>[0-9]{10})$")]
    private static partial Regex PacketNumber();
}

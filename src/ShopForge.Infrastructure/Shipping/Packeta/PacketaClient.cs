using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ShopForge.Infrastructure.Shipping.Packeta;

// Everything that depends on reading Packeta's documentation correctly, in one file and nowhere else: the
// address, the field names and the shape of the answer. Everything around it is ours and is tested (D-155).
// Nothing here has ever been sent to Packeta; checking it against the current API reference is one file to read.
internal interface IPacketaClient
{
    // Null means Packeta says this is not a point the shopper may have chosen. Throwing means Packeta could not
    // be asked, which is a different answer and must not be mistaken for a refusal (D-160).
    Task<PacketaPoint?> ValidatePointAsync(PacketaAccount account, PacketaPointChoice choice, CancellationToken cancellationToken);
}

// What the widget was opened with, repeated to the validator so the two are asking about the same thing: a
// point that is real but belongs to another country or another kind of place is not one this method may use.
internal sealed record PacketaPointChoice(string PointId, string Country, string Vendor);

// The server-side half of the credentials. The widget key is not here on purpose: it belongs in the browser and
// never needs to reach this call, so there is nowhere for the two to be confused.
internal sealed record PacketaAccount(string ApiPassword);

internal sealed record PacketaPoint(string Id, string Name, string Street, string City, string Zip, string Country);

internal sealed class PacketaHttpClient(HttpClient client) : IPacketaClient
{
    // The password travels in the path because that is how Packeta's REST API identifies the account. That
    // makes the URL itself a credential: it must not be logged, which is why this client's logging is off
    // where it is registered.
    public async Task<PacketaPoint?> ValidatePointAsync(PacketaAccount account, PacketaPointChoice choice, CancellationToken cancellationToken)
    {
        var path = $"v5/{account.ApiPassword}/pickup-point/validate"
            + $"?pointId={Uri.EscapeDataString(choice.PointId)}"
            + $"&country={Uri.EscapeDataString(choice.Country)}"
            + $"&vendor={Uri.EscapeDataString(choice.Vendor)}";

        // An answer of "no" is a refusal; no answer at all is left to throw, because an order must not be
        // placed against a point nobody has checked.
        using var response = await client.GetAsync(path, cancellationToken);

        response.EnsureSuccessStatusCode();

        var checked_ = await response.Content.ReadFromJsonAsync<Validation>(cancellationToken);

        return checked_ is { IsValid: true, Id: { Length: > 0 } id }
            ? new PacketaPoint(
                id,
                checked_.Name ?? id,
                checked_.Street ?? string.Empty,
                checked_.City ?? string.Empty,
                checked_.Zip ?? string.Empty,
                checked_.Country ?? choice.Country)
            : null;
    }

    private sealed record Validation(
        [property: JsonPropertyName("isValid")] bool IsValid,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("street")] string? Street,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("zip")] string? Zip,
        [property: JsonPropertyName("country")] string? Country);
}

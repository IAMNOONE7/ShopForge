using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ShopForge.Infrastructure.Shipping.Packeta;

// Everything that depends on reading Packeta's documentation correctly, in one file and nowhere else: the
// address, the field names and the shape of the answer. Everything around it is ours and is tested (D-155).
// Nothing here has ever been sent to Packeta; checking it against the current API reference is one file to read.
internal interface IPacketaClient
{
    Task<PacketaPoint?> FindPointAsync(PacketaAccount account, string pointId, CancellationToken cancellationToken);
}

// The server-side half of the credentials. The widget key is not here on purpose: it belongs in the browser and
// never needs to reach this call, so there is nowhere for the two to be confused.
internal sealed record PacketaAccount(string ApiPassword);

internal sealed record PacketaPoint(string Id, string Name, string Street, string City, string Zip, string Country);

internal sealed class PacketaHttpClient(HttpClient client) : IPacketaClient
{
    // The password travels in the path because that is how Packeta's REST API identifies the account. That
    // makes the URL itself a credential: it must not be logged, which is why this client's logging is off
    // where it is registered.
    public async Task<PacketaPoint?> FindPointAsync(PacketaAccount account, string pointId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"v5/{account.ApiPassword}/branch/{pointId}.json", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var branch = await response.Content.ReadFromJsonAsync<Branch>(cancellationToken);

        return branch is { Id: { Length: > 0 } id }
            ? new PacketaPoint(id, branch.Name ?? id, branch.Street ?? string.Empty, branch.City ?? string.Empty, branch.Zip ?? string.Empty, branch.Country ?? string.Empty)
            : null;
    }

    private sealed record Branch(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("street")] string? Street,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("zip")] string? Zip,
        [property: JsonPropertyName("country")] string? Country);
}

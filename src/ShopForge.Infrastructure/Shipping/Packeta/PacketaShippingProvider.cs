using Microsoft.Extensions.Logging;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Security;
using ShopForge.Shared.Shipping;

namespace ShopForge.Infrastructure.Shipping.Packeta;

// A carrier whose boxes are chosen in its own map (D-158). It is registered whatever the deployment holds and is
// simply not offered by a store that has connected no account, the way Comgate is (D-155).
//
// Two credentials, kept apart on purpose. The widget key is published to the browser because that is what it is
// for; the API password is read here, from the vault, and has no way of reaching a response (D-139).
internal sealed class PacketaShippingProvider(
    IPacketaClient packeta,
    IProviderConnections connections,
    ISecretStore secrets,
    ILogger<PacketaShippingProvider> logger) : IShippingProvider
{
    public const string ProviderKey = "packeta";

    // One country and one kind of place, which is the whole of what this stage offers. The widget is opened
    // with the same pair in the browser, and the validator is asked with it again here so that a point which
    // is real but is a shop counter in Slovakia cannot be passed off as a Czech Z-BOX.
    public const string Country = "cz";
    public const string Vendor = "zbox";

    public string Key => ProviderKey;

    public bool NeedsConnection => true;

    public PickupPointChoice PickupPoints => PickupPointChoice.InTheCarriersMap;

    // Thousands of boxes, chosen in Packeta's own map. There is no list to hand over and asking for one would
    // be a call for nothing.
    public Task<IReadOnlyList<PickupPoint>> FindPickupPointsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PickupPoint>>([]);

    public async Task<PickupPoint?> FindPickupPointAsync(string code, CancellationToken cancellationToken)
    {
        if (await AccountAsync(cancellationToken) is not { } account)
        {
            logger.LogWarning("A Packeta point was chosen in a store with no usable Packeta connection.");

            return null;
        }

        return await packeta.ValidatePointAsync(account, new PacketaPointChoice(code, Country, Vendor), cancellationToken) is { } point
            ? new PickupPoint(point.Id, point.Name, point.Street, point.City, point.Zip, point.Country)
            : null;
    }

    // Booking the parcel and printing its label are Packeta's API and are not written yet, so a shipment here is
    // one the store booked in Packeta's own portal and is recording. Nothing is sent to Packeta by this.
    public Task<ShipmentDetails> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ShipmentDetails("Packeta", request.TrackingNumber ?? string.Empty, TrackingUrl: null));

    private async Task<PacketaAccount?> AccountAsync(CancellationToken cancellationToken)
    {
        if (await connections.FindAsync(Key, cancellationToken) is not { } connection || !connection.IsUsable)
        {
            return null;
        }

        return await secrets.FindAsync(connection.SecretName!, cancellationToken) is { Length: > 0 } password
            ? new PacketaAccount(password)
            : null;
    }
}

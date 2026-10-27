using Microsoft.Extensions.Logging;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Shipping;
using ShopForge.Shared.Stores;

namespace ShopForge.Infrastructure.Shipping.Packeta;

// A carrier whose boxes are chosen in its own map (D-158). It is registered whatever the deployment holds and is
// simply not offered by a store that has connected no account, the way Comgate is (D-155).
//
// Two credentials, kept apart on purpose. The map key is published to the browser because that is what it is for,
// and the point check asks with it too; the API password stays in the vault for booking, which is V8's, and has no
// way of reaching a response (D-139, D-213).
internal sealed class PacketaShippingProvider(
    IPacketaClient packeta,
    IProviderConnections connections,
    ICurrentStoreSettings storeSettings,
    ILogger<PacketaShippingProvider> logger) : IShippingProvider
{
    public const string ProviderKey = "packeta";

    // One country and one kind of place, which is the whole of what launch offers: Czech Z-BOXes. The widget is opened
    // with the same pair in the browser, and the validator is asked with it again here, so that a point which is real
    // but is a shop counter in Slovakia cannot be passed off as a Czech Z-BOX.
    public const string Country = "cz";
    public const string Group = "zbox";

    public string Key => ProviderKey;

    public bool NeedsConnection => true;

    public PickupPointChoice PickupPoints => PickupPointChoice.InTheCarriersMap;

    // Thousands of boxes, chosen in Packeta's own map. There is no list to hand over and asking for one would
    // be a call for nothing.
    public Task<IReadOnlyList<PickupPoint>> FindPickupPointsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PickupPoint>>([]);

    public async Task<PickupPoint?> FindPickupPointAsync(string code, Parcel parcel, CancellationToken cancellationToken)
    {
        var choice = new PacketaPointChoice(
            code,
            Country,
            Group,
            parcel.WeightGrams / 1000m,
            Parcel.Centimetres(parcel.LongestMm),
            Parcel.Centimetres(parcel.MiddleMm),
            Parcel.Centimetres(parcel.ShortestMm));

        if (!choice.CouldBeBooked)
        {
            return null;
        }

        // A store without the map key could not have opened the map, and readiness refuses to publish it (D-213).
        if (await connections.FindAsync(Key, cancellationToken) is not { IsUsable: true, PublishableKey: { Length: > 0 } apiKey })
        {
            logger.LogWarning("A Packeta point was chosen in a store with no usable Packeta connection or no map key.");

            return null;
        }

        var check = await packeta.ValidatePointAsync(apiKey, choice, cancellationToken);

        if (check.Point is not { } point)
        {
            logger.LogInformation(
                "Packeta refused point {PointId}: {Refusals}.",
                choice.PointId,
                check.Refusals.Count > 0 ? string.Join(", ", check.Refusals) : "no reason given");

            return null;
        }

        return new PickupPoint(point.Id, point.Name, point.Street, point.City, point.Zip, point.Country);
    }

    // Booking the parcel and printing its label are V8's, written against what D-213 records, so a shipment here is one
    // the store booked in Packeta's own portal and is recording. Nothing is sent to Packeta by this. The number typed in
    // becomes a link to Packeta's tracking page when it is a Packeta packet's, in the store's language.
    public async Task<ShipmentDetails> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken)
    {
        var number = request.TrackingNumber ?? string.Empty;
        var settings = await storeSettings.GetAsync(cancellationToken);

        return new ShipmentDetails("Packeta", number, PacketaTracking.Url(number, Language(settings.Culture)));
    }

    private static string Language(string culture) => culture.Split('-')[0].ToLowerInvariant();
}

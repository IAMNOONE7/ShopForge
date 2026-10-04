namespace ShopForge.Shared.Shipping;

// Shipping methods name the provider that carries them, the way payment methods do (D-061). Stage 10 ships the
// store's own; a carrier adapter implements the same interface.
public interface IShippingProvider
{
    string Key { get; }

    // Where the shopper chooses a point: from a list this provider can produce, or in the carrier's own map.
    // A carrier with thousands of boxes does not hand them over to be listed, and asking it to is the mistake
    // this says out loud rather than leaving to an empty list nobody can explain.
    PickupPointChoice PickupPoints => PickupPointChoice.FromOurList;

    Task<IReadOnlyList<PickupPoint>> FindPickupPointsAsync(CancellationToken cancellationToken);

    // The point the shopper says they chose, as the provider knows it: null when it is not one of theirs or is
    // not open. Nothing a browser sends about a point is kept except its code, because the rest is the
    // provider's to state.
    Task<PickupPoint?> FindPickupPointAsync(string code, CancellationToken cancellationToken);

    // Whether the carrier will take this parcel at all. The store's own limits belong to the method and are
    // checked before this is asked (D-145); this is the carrier's own say on top of them.
    Task<bool> CanCarryAsync(Parcel parcel, CancellationToken cancellationToken) => Task.FromResult(true);

    Task<ShipmentDetails> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken);
}

public enum PickupPointChoice
{
    FromOurList,
    InTheCarriersMap,
}

// What the cart comes to, as far as anyone deciding whether it can be carried needs to know. The weight is
// unknown when any form in the cart has none recorded, which is a different thing from weighing nothing, and
// the destination is unknown while the shopper is still being shown what their choices are.
public sealed record Parcel(int? WeightGrams, string? DestinationCountry);

public sealed record PickupPoint(string Code, string Name, string Line1, string City, string PostalCode, string Country);

public sealed record ShipmentRequest(
    string OrderNumber,
    string MethodName,
    string RecipientName,
    string Line1,
    string City,
    string PostalCode,
    string Country,
    string? PickupPointCode,
    string? TrackingNumber);

public sealed record ShipmentDetails(string Carrier, string TrackingNumber, string? TrackingUrl);

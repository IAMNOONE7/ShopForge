namespace ShopForge.Shared.Shipping;

// Shipping methods name the provider that carries them, the way payment methods do (D-061). Stage 10 ships the
// store's own; a carrier adapter implements the same interface.
public interface IShippingProvider
{
    string Key { get; }

    Task<IReadOnlyList<PickupPoint>> FindPickupPointsAsync(CancellationToken cancellationToken);

    Task<ShipmentDetails> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken);
}

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

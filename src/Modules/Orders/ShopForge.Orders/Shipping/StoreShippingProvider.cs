using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Shipping;

namespace ShopForge.Orders.Shipping;

// The store carries the parcel itself: the pickup points are its own and the tracking number is what it was given at
// the counter (D-063). A carrier adapter implements the same interface without Orders changing.
internal sealed class StoreShippingProvider(DbContext dbContext) : IShippingProvider
{
    public const string ProviderKey = "manual";

    public string Key => ProviderKey;

    public async Task<IReadOnlyList<PickupPoint>> FindPickupPointsAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<StorePickupPoint>()
            .Where(point => point.IsActive)
            .OrderBy(point => point.Name)
            .Select(point => new PickupPoint(
                point.Code,
                point.Name,
                point.Address.Line1,
                point.Address.City,
                point.Address.PostalCode,
                point.Address.Country))
            .ToListAsync(cancellationToken);

    public Task<ShipmentDetails> CreateShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ShipmentDetails(request.MethodName, request.TrackingNumber ?? string.Empty, TrackingUrl: null));
}

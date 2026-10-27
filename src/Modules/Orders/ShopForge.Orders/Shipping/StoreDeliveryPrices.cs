using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Storefront;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Shipping;

namespace ShopForge.Orders.Shipping;

// What delivery costs at the least, shown before the checkout (D-220): a flat method's price, or the lowest band of a
// method priced by the parcel. Only the methods a shopper could choose count, the way the checkout offers them:
// switched on, with a provider the store can use, and for one priced by the parcel a service in use and prices that
// are not the invented placeholders.
internal sealed class StoreDeliveryPrices(
    DbContext dbContext,
    IEnumerable<IShippingProvider> providers,
    IProviderConnections connections) : IStoreDeliveryPrices
{
    public async Task<decimal?> CheapestAsync(CancellationToken cancellationToken)
    {
        var usable = await CheckoutEndpoints.UsableProvidersAsync(providers, connections, cancellationToken);
        var methods = await dbContext.Set<ShippingMethod>()
            .AsNoTracking()
            .Where(method => method.IsActive && method.RatesFrom != RateSource.Placeholder && usable.Contains(method.ProviderKey))
            .ToListAsync(cancellationToken);
        var keys = methods.Where(method => method.Pricing == ShippingPricing.PerParcel).Select(method => method.CarrierServiceKey).Distinct().ToList();
        var inUse = await dbContext.Set<CarrierService>()
            .AsNoTracking()
            .Where(service => keys.Contains(service.Key) && service.IsActive)
            .Select(service => new { service.Key, service.ProviderKey })
            .ToListAsync(cancellationToken);

        var prices = methods
            .Select(method => method.Pricing == ShippingPricing.Flat
                ? method.Price
                : method.RateBands.Count > 0 && inUse.Any(service => service.Key == method.CarrierServiceKey && service.ProviderKey == method.ProviderKey)
                    ? method.RateBands.Min(band => band.Price)
                    : (decimal?)null)
            .OfType<decimal>()
            .ToList();

        return prices.Count == 0 ? null : prices.Min();
    }
}

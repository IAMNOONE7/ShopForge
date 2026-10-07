using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Shipping;

namespace ShopForge.Orders.Shipping;

// The store's own methods, as rates somebody else can publish. The price is flat per method (D-145), so each
// method the store serves a country with is one rate in that country.
internal sealed class StoreShippingRates(DbContext dbContext) : IStoreShippingRates
{
    public async Task<IReadOnlyList<ShippingRate>> FindAsync(CancellationToken cancellationToken)
    {
        var methods = await dbContext.Set<ShippingMethod>()
            .AsNoTracking()
            .Where(method => method.IsActive)
            .OrderBy(method => method.Name)
            .Select(method => new { method.Name, method.Price, method.Countries })
            .ToListAsync(cancellationToken);

        // A method with no countries named serves somewhere nobody has written down. Saying nothing is the
        // honest answer; a feed that invents a country is a promise the shop never made.
        return
        [
            .. methods.SelectMany(method => method.Countries.Select(country => new ShippingRate(country, method.Name, method.Price))),
        ];
    }
}

using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Catalog;

namespace ShopForge.Orders.Catalog;

internal sealed class SoldListings(DbContext dbContext) : ISoldListings
{
    // An order line names the listing it sold. The line keeps its own copy of the name and the price, so the
    // paperwork survives whatever happens to the catalogue — but the row it points at should not vanish from
    // under it, which is why this question is asked before a deletion (D-180).
    public Task<bool> HasBeenOrderedAsync(Guid storeProductId, CancellationToken cancellationToken) =>
        dbContext.Set<Order>()
            .SelectMany(order => order.Lines)
            .AnyAsync(line => line.StoreProductId == storeProductId, cancellationToken);
}

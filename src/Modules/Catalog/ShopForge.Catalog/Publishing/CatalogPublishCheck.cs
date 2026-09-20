using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Stores;

namespace ShopForge.Catalog.Publishing;

internal sealed class CatalogPublishCheck(DbContext dbContext) : IStorePublishCheck
{
    public async Task<string?> FindProblemAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<StoreProduct>().AnyAsync(storeProduct => storeProduct.IsVisible, cancellationToken)
            ? null
            : "The store has no visible products.";
}

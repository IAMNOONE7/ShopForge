using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Publishing;

internal sealed class TenantProductLookup(DbContext dbContext) : ITenantProducts
{
    public Task<bool> ExistsAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.Set<Product>().AnyAsync(product => product.Id == productId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Guid>> ProductIdsAsync(
        IReadOnlyCollection<Guid> storeProductIds,
        CancellationToken cancellationToken) =>
        await dbContext.Set<StoreProduct>()
            .Where(storeProduct => storeProductIds.Contains(storeProduct.Id))
            .ToDictionaryAsync(storeProduct => storeProduct.Id, storeProduct => storeProduct.ProductId, cancellationToken);
}

internal sealed class ProductUsage(DbContext dbContext) : ITenantUsage
{
    public async Task<IReadOnlyList<UsageCount>> CountAsync(IReadOnlyCollection<Guid> storeIds, CancellationToken cancellationToken) =>
    [
        new UsageCount("products", await dbContext.Set<Product>().CountAsync(cancellationToken)),
        new UsageCount("listings", await dbContext.Set<StoreProduct>()
            .IgnoreQueryFilters([TenancyFilters.Store])
            .CountAsync(listing => storeIds.Contains(listing.StoreId), cancellationToken)),
    ];
}

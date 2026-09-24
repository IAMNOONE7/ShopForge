namespace ShopForge.Shared.Catalog;

// Modules that key their own data by product id use this to reject ids that are not in the tenant's catalog.
public interface ITenantProducts
{
    Task<bool> ExistsAsync(Guid productId, CancellationToken cancellationToken);

    // Which product of the catalog each listing sells, on sale or not: goods come back to stock even when the
    // listing they were bought from has since been taken down.
    Task<IReadOnlyDictionary<Guid, Guid>> ProductIdsAsync(IReadOnlyCollection<Guid> storeProductIds, CancellationToken cancellationToken);
}

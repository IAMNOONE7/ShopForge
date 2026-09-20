namespace ShopForge.Shared.Catalog;

// Modules that key their own data by product id use this to reject ids that are not in the tenant's catalog.
public interface ITenantProducts
{
    Task<bool> ExistsAsync(Guid productId, CancellationToken cancellationToken);
}

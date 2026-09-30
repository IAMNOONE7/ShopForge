namespace ShopForge.Shared.Catalog;

// Modules that key their own data by variant id use this to reject ids that are not in the tenant's catalog.
public interface ITenantProducts
{
    Task<bool> VariantExistsAsync(Guid variantId, CancellationToken cancellationToken);
}

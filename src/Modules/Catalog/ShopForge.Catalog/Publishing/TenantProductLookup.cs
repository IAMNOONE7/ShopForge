using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Catalog;

namespace ShopForge.Catalog.Publishing;

internal sealed class TenantProductLookup(DbContext dbContext) : ITenantProducts
{
    public Task<bool> ExistsAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.Set<Product>().AnyAsync(product => product.Id == productId, cancellationToken);
}

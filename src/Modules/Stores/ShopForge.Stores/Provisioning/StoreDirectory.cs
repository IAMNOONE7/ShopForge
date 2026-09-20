using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Provisioning;

// Background work has no store context yet, so this is the one query that reads across tenants; callers scope
// themselves to each store before touching its data.
internal sealed class StoreDirectory(DbContext dbContext) : IStoreDirectory
{
    public async Task<IReadOnlyList<StoreReference>> AllAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<Store>()
            .IgnoreQueryFilters()
            .OrderBy(store => store.Id)
            .Select(store => new StoreReference(store.Id, store.TenantId))
            .ToListAsync(cancellationToken);
}

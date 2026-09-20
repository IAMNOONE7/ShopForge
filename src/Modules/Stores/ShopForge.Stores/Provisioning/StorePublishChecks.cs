using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Provisioning;

internal sealed class StoreBrandingPublishCheck(DbContext dbContext, IStoreContext storeContext) : IStorePublishCheck
{
    public async Task<string?> FindProblemAsync(CancellationToken cancellationToken)
    {
        var hasLogo = await dbContext.Set<Store>()
            .AnyAsync(store => store.Id == storeContext.StoreId && store.LogoPath != null, cancellationToken);

        return hasLogo ? null : "The store has no logo.";
    }
}

using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Provisioning;

internal sealed class StoreBrandingPublishCheck(DbContext dbContext, IStoreContext storeContext) : IStorePublishCheck
{
    public async Task<IReadOnlyList<StoreReadiness>> CheckAsync(CancellationToken cancellationToken)
    {
        var hasLogo = await dbContext.Set<Store>()
            .AnyAsync(store => store.Id == storeContext.StoreId && store.LogoPath != null, cancellationToken);

        return [hasLogo ? StoreReadiness.Ready("store.logo") : StoreReadiness.NotReady("store.logo", "The store has no logo.")];
    }
}

// A shop that takes money issues invoices, and an invoice names a seller (D-078).
internal sealed class StoreCompanyPublishCheck(DbContext dbContext, IStoreContext storeContext) : IStorePublishCheck
{
    public async Task<IReadOnlyList<StoreReadiness>> CheckAsync(CancellationToken cancellationToken)
    {
        var hasCompany = await dbContext.Set<Store>()
            .AnyAsync(store => store.Id == storeContext.StoreId && store.Company != null, cancellationToken);

        return [hasCompany
            ? StoreReadiness.Ready("store.company")
            : StoreReadiness.NotReady("store.company", "The store has no company details.")];
    }
}

// The primary domain is where the store calls itself home: mail and documents link to it, and a store resolves
// on a name only once it is proved. A store given its platform subdomain has one from the moment it exists, so
// this is about what happens afterwards — a domain removed, or a primary that somehow lost its proof (D-189).
internal sealed class StoreDomainPublishCheck(DbContext dbContext, IStoreContext storeContext) : IStorePublishCheck
{
    public async Task<IReadOnlyList<StoreReadiness>> CheckAsync(CancellationToken cancellationToken)
    {
        var hasProvedPrimary = await dbContext.Set<StoreDomain>()
            .IgnoreQueryFilters([TenancyFilters.Store])
            .AnyAsync(domain => domain.StoreId == storeContext.StoreId && domain.IsPrimary && domain.VerifiedAt != null, cancellationToken);

        return [hasProvedPrimary
            ? StoreReadiness.Ready("store.primary-domain")
            : StoreReadiness.NotReady("store.primary-domain", "The store has no proved primary domain, so nothing would resolve to it.")];
    }
}

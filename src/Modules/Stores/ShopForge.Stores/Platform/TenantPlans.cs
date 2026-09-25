using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Platform;

// The caps the current tenant is under. A company that has not been put on a plan is on the default one, so there
// is no such thing as a tenant without caps by accident (D-108).
internal sealed class TenantPlans(DbContext dbContext, IStoreContext storeContext) : ITenantLimits
{
    public async Task<int?> MaxAsync(TenantResource resource, CancellationToken cancellationToken)
    {
        var plan = await ForCurrentTenantAsync(cancellationToken);

        return resource switch
        {
            TenantResource.Stores => plan?.MaxStores,
            TenantResource.Products => plan?.MaxProducts,
            _ => null,
        };
    }

    // Store-owned rows are scoped by the stores of this company, not by a tenant column they do not have (D-105).
    public async Task<IReadOnlyList<Guid>> StoreIdsAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<Store>()
            .IgnoreQueryFilters([TenancyFilters.Store])
            .Select(store => store.Id)
            .ToListAsync(cancellationToken);

    public async Task<Plan?> ForCurrentTenantAsync(CancellationToken cancellationToken)
    {
        var planId = await dbContext.Set<Tenant>()
            .Where(tenant => tenant.Id == storeContext.TenantId)
            .Select(tenant => tenant.PlanId)
            .SingleOrDefaultAsync(cancellationToken);

        return planId is null
            ? await dbContext.Set<Plan>().SingleOrDefaultAsync(plan => plan.IsDefault, cancellationToken)
            : await dbContext.Set<Plan>().SingleOrDefaultAsync(plan => plan.Id == planId, cancellationToken);
    }
}

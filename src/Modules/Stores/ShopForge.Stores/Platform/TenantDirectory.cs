using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Stores;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Platform;

internal sealed class TenantDirectory(DbContext dbContext) : ITenantDirectory
{
    public Task<bool> IsActiveAsync(Guid tenantId, CancellationToken cancellationToken) =>
        dbContext.Set<Tenant>().AnyAsync(tenant => tenant.Id == tenantId && tenant.Status == TenantStatus.Active, cancellationToken);
}

internal sealed class StoreUsage : ITenantUsage
{
    public Task<IReadOnlyList<UsageCount>> CountAsync(IReadOnlyCollection<Guid> storeIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UsageCount>>([new UsageCount("stores", storeIds.Count)]);
}

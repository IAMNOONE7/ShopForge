using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Caching;
using ShopForge.Shared.Dns;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Resolution;

// A host name is looked up on every storefront request, so the answer is kept. Every replica keeps its own, and a
// change that makes a host stop resolving — a company suspended, a store taken offline, a domain removed — is
// forgotten on all of them as soon as it is saved, not five minutes later (D-202).
internal sealed class StoreResolver(DbContext dbContext, IReplicaCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<ResolvedStore?> ResolveAsync(string? host, CancellationToken cancellationToken)
    {
        var hostName = HostNames.Normalize(host);

        if (hostName is null)
        {
            return null;
        }

        // Only hits are kept; keeping misses would let arbitrary Host headers grow the cache.
        return await cache.GetAsync(
            CacheKey(hostName),
            CacheDuration,
            looking => LookUpAsync(hostName, looking),
            store => store is not null,
            cancellationToken);
    }

    private Task<ResolvedStore?> LookUpAsync(string hostName, CancellationToken cancellationToken) =>
        (from domain in dbContext.Set<StoreDomain>().IgnoreQueryFilters()
         join owner in dbContext.Set<Store>().IgnoreQueryFilters() on domain.StoreId equals owner.Id
         join tenant in dbContext.Set<Tenant>() on owner.TenantId equals tenant.Id
         where domain.HostName == hostName
             && domain.VerifiedAt != null
             && owner.Status == StoreStatus.Published
             && tenant.Status == TenantStatus.Active
         select new ResolvedStore(owner.Id, owner.TenantId))
        .SingleOrDefaultAsync(cancellationToken);

    public Task<ResolvedStore?> FindForTenantAsync(Guid storeId, Guid tenantId, CancellationToken cancellationToken) =>
        dbContext.Set<Store>()
            .IgnoreQueryFilters()
            .Where(store => store.Id == storeId && store.TenantId == tenantId)
            .Select(store => new ResolvedStore(store.Id, store.TenantId))
            .SingleOrDefaultAsync(cancellationToken);

    // Every host of a company, so suspending or resuming it can drop what the cache still believes.
    public async Task ForgetTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var hostNames = await (
                from domain in dbContext.Set<StoreDomain>().IgnoreQueryFilters()
                join owner in dbContext.Set<Store>().IgnoreQueryFilters() on domain.StoreId equals owner.Id
                where owner.TenantId == tenantId
                select domain.HostName)
            .ToListAsync(cancellationToken);

        await ForgetAsync(hostNames, cancellationToken);
    }

    // After the change is saved, on this replica and every other.
    public Task ForgetAsync(IEnumerable<string> hostNames, CancellationToken cancellationToken) =>
        cache.ForgetAsync([.. hostNames.Select(CacheKey)], cancellationToken);

    private static string CacheKey(string hostName) => $"stores:host:{hostName}";
}

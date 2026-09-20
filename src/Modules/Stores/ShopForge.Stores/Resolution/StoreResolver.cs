using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Resolution;

internal sealed class StoreResolver(DbContext dbContext, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<ResolvedStore?> ResolveAsync(string? host, CancellationToken cancellationToken)
    {
        var hostName = HostNames.Normalize(host);

        if (hostName is null)
        {
            return null;
        }

        var cacheKey = CacheKey(hostName);

        if (cache.TryGetValue(cacheKey, out ResolvedStore? store))
        {
            return store;
        }

        store = await (
                from domain in dbContext.Set<StoreDomain>().IgnoreQueryFilters()
                join owner in dbContext.Set<Store>().IgnoreQueryFilters() on domain.StoreId equals owner.Id
                where domain.HostName == hostName && owner.Status == StoreStatus.Published
                select new ResolvedStore(owner.Id, owner.TenantId))
            .SingleOrDefaultAsync(cancellationToken);

        // Only hits are cached; caching misses would let arbitrary Host headers grow the cache.
        if (store is not null)
        {
            cache.Set(cacheKey, store, CacheDuration);
        }

        return store;
    }

    public Task<ResolvedStore?> FindForTenantAsync(Guid storeId, Guid tenantId, CancellationToken cancellationToken) =>
        dbContext.Set<Store>()
            .IgnoreQueryFilters()
            .Where(store => store.Id == storeId && store.TenantId == tenantId)
            .Select(store => new ResolvedStore(store.Id, store.TenantId))
            .SingleOrDefaultAsync(cancellationToken);

    public void Forget(IEnumerable<string> hostNames)
    {
        foreach (var hostName in hostNames)
        {
            cache.Remove(CacheKey(hostName));
        }
    }

    private static string CacheKey(string hostName) => $"stores:host:{hostName}";
}

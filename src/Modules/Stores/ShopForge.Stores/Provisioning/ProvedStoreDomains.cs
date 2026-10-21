using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Provisioning;

// A merchant's own names, proved by the record they put in DNS. The platform's subdomain is the store's too, but it is
// in the platform's zone, which no merchant can publish records in (D-189, D-211).
internal sealed class ProvedStoreDomains(DbContext dbContext) : IStoreDomains
{
    public async Task<IReadOnlyList<string>> ProvedAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<StoreDomain>()
            .AsNoTracking()
            .Where(domain => domain.VerifiedAt != null && domain.VerificationToken != null)
            .OrderBy(domain => domain.HostName)
            .Select(domain => domain.HostName)
            .ToListAsync(cancellationToken);
}

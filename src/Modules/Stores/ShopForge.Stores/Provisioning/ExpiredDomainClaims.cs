using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Maintenance;
using ShopForge.Stores.Domain;
using ShopForge.Stores.Resolution;

namespace ShopForge.Stores.Provisioning;

// A claim nobody proves gives the name back. A host name belongs to one store across the whole platform, so an
// unproved claim left lying there is a name its real owner cannot add — and the shop that could not add it has
// no way to find out why. Fourteen days is long enough to reach whoever runs a company's DNS (D-189).
//
// A proved domain is never touched here, and neither is a store's own platform subdomain: that one is proved
// the moment it is created.
internal sealed class ExpiredDomainClaims(DbContext dbContext, StoreResolver resolver, IAuditLog audit, TimeProvider clock) : IStoreMaintenance
{
    public string Name => "stores.expired-domain-claims";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var lapsedBefore = clock.GetUtcNow() - StoreDomain.ClaimHoldsFor;

        var lapsed = await dbContext.Set<StoreDomain>()
            .Where(domain => domain.VerifiedAt == null && domain.ClaimedAt <= lapsedBefore)
            .ToListAsync(cancellationToken);

        if (lapsed.Count == 0)
        {
            return 0;
        }

        foreach (var domain in lapsed)
        {
            // Recorded per name rather than as a count: the one question this answers later is "who had my
            // domain, and when did they stop having it".
            audit.Record("domain.claim-lapsed", domain.HostName, new { domain.ClaimedAt });
        }

        dbContext.RemoveRange(lapsed);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Nothing was resolving to these — an unproved name is not served — but the cache holds misses nowhere
        // and hits for proved names only, so forgetting them is cheap and keeps the two in step.
        resolver.Forget(lapsed.Select(domain => domain.HostName));

        return lapsed.Count;
    }
}

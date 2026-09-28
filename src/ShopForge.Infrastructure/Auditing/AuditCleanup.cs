using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Auditing;

// The record answers disputes and support questions, which do not arrive years later, and every entry holds the
// address the request came from. A year is long enough to be useful and short enough to be defensible; it wants
// to be per plan one day rather than a constant (D-118).
internal sealed class AuditCleanup(DbContext dbContext, TimeProvider clock) : IMaintenanceOutsideStores
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    public string Name => "Audit entries";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var before = clock.GetUtcNow() - Lifetime;

        // Append-only stops an endpoint from rewriting history; a retention sweep is the one thing that removes
        // an entry, and it removes it whole rather than editing it (D-116).
        return await dbContext.Set<AuditEntry>()
            .IgnoreQueryFilters([TenancyFilters.Tenant])
            .Where(entry => entry.RecordedAt < before)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

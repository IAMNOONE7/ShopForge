using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Maintenance;

namespace ShopForge.Infrastructure.Idempotency;

// A key is only worth keeping while the caller might still retry with it. A day covers a dropped connection, a
// phone that came back onto a signal, and a client library's own retries; beyond that the row is a copy of an
// order confirmation nobody will ask for again (D-118, D-131).
internal sealed class IdempotencyCleanup(DbContext dbContext, TimeProvider clock) : IMaintenanceOutsideStores
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    public string Name => "Idempotency keys";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var before = clock.GetUtcNow() - Lifetime;

        return await dbContext.Set<IdempotentRequest>()
            .IgnoreQueryFilters()
            .Where(request => request.ClaimedAt < before)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

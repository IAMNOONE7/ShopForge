using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Maintenance;

namespace ShopForge.Infrastructure.Messaging;

// A delivered message has done its job, and its payload holds whatever the event carried — an address, a name, an
// order number. Keeping it for a month is enough to answer "did that e-mail go out?"; keeping it forever would
// quietly outlive a customer's erasure (D-117, D-118).
//
// Messages that gave up are left alone: a dead letter is waiting for a person, not for a sweep (D-068).
internal sealed class OutboxCleanup(DbContext dbContext, TimeProvider clock) : IMaintenanceOutsideStores
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public string Name => "Delivered messages";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var before = clock.GetUtcNow() - Lifetime;

        return await dbContext.Set<OutboxMessage>()
            .IgnoreQueryFilters()
            .Where(message => message.Status == OutboxStatus.Processed && message.ProcessedAt < before)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

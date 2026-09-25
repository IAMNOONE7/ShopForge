using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Messaging;

// What the admin can see and do about messages that gave up (D-068): the dead letter queue, without a broker.
// A store sees its own through the query filter; the ones belonging to no store are the platform's to look after.
public sealed class OutboxAdmin(DbContext dbContext, IServiceProvider services, TimeProvider clock)
{
    public Task<IReadOnlyList<FailedMessage>> FailedAsync(CancellationToken cancellationToken) =>
        FailedAsync(dbContext.Set<OutboxMessage>(), cancellationToken);

    public Task<IReadOnlyList<FailedMessage>> FailedOutsideStoresAsync(CancellationToken cancellationToken) =>
        FailedAsync(OutsideStores(), cancellationToken);

    public async Task<bool> RequeueAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await dbContext.Set<OutboxMessage>()
            .SingleOrDefaultAsync(candidate => candidate.Id == messageId && candidate.Status == OutboxStatus.Failed, cancellationToken);

        if (message is null)
        {
            return false;
        }

        message.Requeue(clock.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> RequeueOutsideStoresAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var found = await OutsideStores()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == messageId && candidate.Status == OutboxStatus.Failed, cancellationToken);

        if (found is null)
        {
            return false;
        }

        // A message with no store may still name a company, and the save guard holds it to that one, so the change
        // is made from inside it rather than from the platform's empty scope (D-111).
        await using var scope = services.CreateAsyncScope();

        if (found.TenantId is { } tenantId)
        {
            scope.ServiceProvider.GetRequiredService<StoreContext>().SetTenant(tenantId);
        }

        var scoped = scope.ServiceProvider.GetRequiredService<DbContext>();
        var message = await scoped.Set<OutboxMessage>()
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == found.Id, cancellationToken);

        message.Requeue(clock.GetUtcNow());
        await scoped.SaveChangesAsync(cancellationToken);

        return true;
    }

    private IQueryable<OutboxMessage> OutsideStores() =>
        dbContext.Set<OutboxMessage>()
            .IgnoreQueryFilters([TenancyFilters.Store])
            .Where(message => message.StoreId == null);

    private static async Task<IReadOnlyList<FailedMessage>> FailedAsync(IQueryable<OutboxMessage> messages, CancellationToken cancellationToken) =>
        await messages
            .AsNoTracking()
            .Where(message => message.Status == OutboxStatus.Failed)
            .OrderByDescending(message => message.CreatedAt)
            .Take(100)
            .Select(message => new FailedMessage(message.Id, message.Type, message.Attempts, message.CreatedAt, message.Error))
            .ToListAsync(cancellationToken);
}

public sealed record FailedMessage(Guid Id, string Type, int Attempts, DateTimeOffset CreatedAt, string? Error);

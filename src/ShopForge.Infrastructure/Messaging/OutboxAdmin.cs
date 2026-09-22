using Microsoft.EntityFrameworkCore;

namespace ShopForge.Infrastructure.Messaging;

// What the admin can see and do about messages that gave up (D-068): the dead letter queue, without a broker.
public sealed class OutboxAdmin(DbContext dbContext, TimeProvider clock)
{
    public async Task<IReadOnlyList<FailedMessage>> FailedAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(message => message.Status == OutboxStatus.Failed)
            .OrderByDescending(message => message.CreatedAt)
            .Take(100)
            .Select(message => new FailedMessage(message.Id, message.Type, message.Attempts, message.CreatedAt, message.Error))
            .ToListAsync(cancellationToken);

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
}

public sealed record FailedMessage(Guid Id, string Type, int Attempts, DateTimeOffset CreatedAt, string? Error);

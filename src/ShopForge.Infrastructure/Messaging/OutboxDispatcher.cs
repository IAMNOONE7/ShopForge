using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopForge.Infrastructure.Diagnostics;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Messaging;

// Claims due messages, runs their handlers inside the store the message names, and lets a failure back off rather
// than block the queue (D-065, D-068).
internal sealed class OutboxDispatcher(IServiceProvider services, OutboxEventTypes eventTypes, TimeProvider clock, ILogger<OutboxDispatcher> logger)
{
    private const int BatchSize = 50;

    // How long a run owns a message it picked up. Several runs can overlap — the worker's tick, a deployment with two
    // instances — and a handler that runs twice would send the same e-mail twice, so the message is leased first.
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    public async Task<int> DispatchAsync(CancellationToken cancellationToken)
    {
        var due = await DueMessagesAsync(cancellationToken);
        var handled = 0;

        foreach (var message in due)
        {
            handled += await HandleAsync(message, cancellationToken) ? 1 : 0;
        }

        return handled;
    }

    private async Task<List<OutboxMessage>> DueMessagesAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var now = clock.GetUtcNow();

        return await scope.ServiceProvider.GetRequiredService<DbContext>().Set<OutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message => message.Status == OutboxStatus.Pending && message.DueAt <= now)
            .OrderBy(message => message.DueAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> HandleAsync(OutboxMessage claimed, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(claimed.StoreId, claimed.TenantId);

        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var now = clock.GetUtcNow();
        var leased = await dbContext.Set<OutboxMessage>()
            .Where(candidate => candidate.Id == claimed.Id && candidate.Status == OutboxStatus.Pending && candidate.DueAt <= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.DueAt, now + Lease), cancellationToken);

        if (leased == 0)
        {
            return false;
        }

        var message = await dbContext.Set<OutboxMessage>().SingleAsync(candidate => candidate.Id == claimed.Id, cancellationToken);

        using var activity = ShopForgeMetrics.ActivitySource.StartActivity($"outbox {message.Type}", ActivityKind.Consumer, message.TraceParent);
        activity?.SetTag("shopforge.store_id", message.StoreId);
        activity?.SetTag("messaging.message.id", message.Id);

        try
        {
            await eventTypes.DeliverAsync(scope.ServiceProvider, message.Type, message.Payload, cancellationToken);
            message.Succeed(clock.GetUtcNow());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Handling outbox message {MessageId} ({Type}) failed on attempt {Attempt}.", message.Id, message.Type, message.Attempts + 1);
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            message.Fail(exception.Message, clock.GetUtcNow());
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return message.Status == OutboxStatus.Processed;
    }
}

internal sealed class OutboxWorker(OutboxDispatcher dispatcher, TimeProvider clock, ILogger<OutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period, clock);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await dispatcher.DispatchAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The outbox run failed; the next one picks the messages up again.");
            }
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Background;

// An order that is never paid must not hold stock forever (D-048). Orders owns the decision; Inventory only gives the
// items back when it is told to.
internal sealed class ExpiredOrders(IServiceProvider services, TimeProvider clock, ILogger<ExpiredOrders> logger)
{
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var stores = await scope.ServiceProvider.GetRequiredService<IStoreDirectory>().AllAsync(cancellationToken);
        var cancelled = 0;

        foreach (var store in stores)
        {
            cancelled += await SweepStoreAsync(store, cancellationToken);
        }

        return cancelled;
    }

    private async Task<int> SweepStoreAsync(StoreReference store, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(store.StoreId, store.TenantId);

        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var stock = scope.ServiceProvider.GetRequiredService<IStockLedger>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
        var now = clock.GetUtcNow();

        var expired = await dbContext.Set<Order>()
            .Where(order => order.Status == OrderStatus.AwaitingPayment && order.ReservationExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var order in expired)
        {
            order.Cancel();
            await stock.ReleaseAsync(order.Number, cancellationToken);
            outbox.Enqueue(new OrderCancelled(order.Number, order.Email, "The order was not paid in time."));
        }

        if (expired.Count == 0)
        {
            return 0;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Cancelled {Count} unpaid orders in store {StoreId} and released their stock.", expired.Count, store.StoreId);

        return expired.Count;
    }
}

internal sealed class ExpiredOrderSweeper(ExpiredOrders expiredOrders, TimeProvider clock, ILogger<ExpiredOrderSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period, clock);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await expiredOrders.SweepAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Sweeping expired orders failed; retrying on the next run.");
            }
        }
    }
}

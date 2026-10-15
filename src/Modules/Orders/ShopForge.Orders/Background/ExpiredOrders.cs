using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Payments;
using ShopForge.Shared.Diagnostics;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Background;

// An order that is never paid must not hold stock forever (D-048). Orders owns the decision; Inventory only gives the
// items back when it is told to.
//
// Before an order with a gateway payment still open is let go, the gateway is asked. A shopper who paid in the last
// minutes, and whose notification was lost, is owed the order rather than a refund (D-199).
//
// Every minute, by one replica (D-202). The claim on each order below is what makes two sweeps safe; one sweep at a
// time is what keeps a gateway from being asked the same question by every replica.
internal sealed class ExpiredOrders(IServiceProvider services, TimeProvider clock, ILogger<ExpiredOrders> logger) : IScheduledPass
{
    // How long past its reservation an order is kept while its gateway cannot be asked about it: long enough for a
    // gateway having a bad minute, not so long that a silent one holds the stock.
    public static readonly TimeSpan AskingGrace = TimeSpan.FromMinutes(10);

    public string Name => "expired-orders";

    public TimeSpan Period => TimeSpan.FromMinutes(1);

    Task IScheduledPass.RunAsync(CancellationToken cancellationToken) => SweepAsync(cancellationToken);

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
        var metrics = scope.ServiceProvider.GetRequiredService<IShopForgeMetrics>();
        var now = clock.GetUtcNow();

        var expired = await dbContext.Set<Order>()
            .AsNoTracking()
            .Where(order => order.Status == OrderStatus.AwaitingPayment && order.ReservationExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return 0;
        }

        // Asked before the transaction below, because an answer is recorded the way every answer is, in a transaction
        // of its own (D-176): a payment confirms the order and a refusal cancels it, and either way the claim below
        // finds the order no longer waiting. Silence keeps it a little longer.
        var unasked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var order in expired)
        {
            if (!await AskAsync(scope.ServiceProvider, order, cancellationToken) && now - order.ReservationExpiresAt < AskingGrace)
            {
                unasked.Add(order.Number);
            }
        }

        // The cancellation, the stock behind it and the letter about it belong together, as they do everywhere else
        // an order changes: a sweep that stops halfway leaves nothing applied.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var cancelled = 0;

        foreach (var order in expired.Where(order => !unasked.Contains(order.Number)))
        {
            // Every instance sweeps, so the order is claimed before anything follows from it; the run that does not
            // get it leaves the stock and the letter to the run that did.
            var claimed = await dbContext.Set<Order>()
                .Where(candidate => candidate.Id == order.Id && candidate.Status == OrderStatus.AwaitingPayment)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, OrderStatus.Cancelled), cancellationToken);

            if (claimed == 0)
            {
                continue;
            }

            await stock.ReleaseAsync(order.Number, cancellationToken);
            outbox.Enqueue(new OrderCancelled(order.Number, order.Email, "The order was not paid in time."));
            metrics.OrderCancelled("expired");
            cancelled++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (cancelled > 0)
        {
            logger.LogInformation("Cancelled {Count} unpaid orders in store {StoreId} and released their stock.", cancelled, store.StoreId);
        }

        return cancelled;
    }

    // Whether every gateway holding an open payment for the order could be asked about it. What one says is recorded;
    // one that does not know the payment, or that the store can no longer reach, has nothing to wait for.
    private async Task<bool> AskAsync(IServiceProvider scoped, Order order, CancellationToken cancellationToken)
    {
        var enquiries = scoped.GetServices<IPaymentEnquiries>().ToDictionary(enquiry => enquiry.Key, StringComparer.Ordinal);
        var open = await scoped.GetRequiredService<DbContext>().Set<PaymentAttempt>()
            .AsNoTracking()
            .Where(attempt => attempt.OrderNumber == order.Number
                && attempt.Reference != null
                && attempt.Status != PaymentAttemptStatus.Paid
                && attempt.Status != PaymentAttemptStatus.Failed)
            .ToListAsync(cancellationToken);
        var asked = true;

        foreach (var attempt in open.Where(attempt => enquiries.ContainsKey(attempt.Provider)))
        {
            try
            {
                var enquiry = new PaymentEnquiry(attempt.StoreId, attempt.OrderNumber, attempt.Reference!, attempt.Amount, attempt.Currency);

                if (await enquiries[attempt.Provider].AskAsync(enquiry, cancellationToken) is { } answer)
                {
                    await scoped.GetRequiredService<PaymentResults>().RecordAsync(attempt.Provider, answer, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    exception,
                    "{Provider} could not be asked about order {OrderNumber} before letting it go; it is kept for now.",
                    attempt.Provider,
                    order.Number);
                asked = false;
            }
        }

        return asked;
    }
}

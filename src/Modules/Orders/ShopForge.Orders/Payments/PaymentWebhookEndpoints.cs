using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Diagnostics;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Payments;

// Payment results arrive here, not from the browser coming back from the provider (D-057). The request carries no
// store: the provider's payload names it, and everything after that runs inside that store's scope.
internal static class PaymentWebhookEndpoints
{
    public static void MapPaymentWebhooks(this IEndpointRouteBuilder payments)
    {
        payments.MapPost("/{provider}", HandleAsync);
    }

    private static async Task<Results<Ok, BadRequest, NotFound>> HandleAsync(
        string provider,
        HttpContext httpContext,
        DbContext dbContext,
        StoreContext storeContext,
        IStoreDirectory stores,
        IStockLedger stock,
        IOutbox outbox,
        IShopForgeMetrics metrics,
        IEnumerable<IPaymentNotifications> notifications,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(PaymentWebhookEndpoints));
        var reader = notifications.SingleOrDefault(candidate => candidate.Key == provider);

        if (reader is null)
        {
            return TypedResults.NotFound();
        }

        var notification = await reader.ReadAsync(httpContext.Request, cancellationToken);

        if (notification is null)
        {
            return TypedResults.BadRequest();
        }

        var store = await stores.FindAsync(notification.StoreId, cancellationToken);

        if (store is null)
        {
            logger.LogWarning("A {Provider} event named store {StoreId}, which does not exist.", provider, notification.StoreId);

            return TypedResults.NotFound();
        }

        storeContext.Set(store.StoreId, store.TenantId);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // A redelivery finds its own record and stops here. Two deliveries at the same moment race for the unique
        // index instead: the loser's 409 makes the provider retry, and that retry takes this path.
        if (await dbContext.Set<PaymentEvent>().AnyAsync(recorded => recorded.Provider == provider && recorded.EventId == notification.EventId, cancellationToken))
        {
            return TypedResults.Ok();
        }

        var order = await dbContext.Set<Order>().SingleOrDefaultAsync(candidate => candidate.Number == notification.OrderNumber, cancellationToken);

        if (order is null)
        {
            logger.LogWarning("A {Provider} event named order {OrderNumber}, which store {StoreId} does not have.", provider, notification.OrderNumber, store.StoreId);

            return TypedResults.NotFound();
        }

        dbContext.Add(new PaymentEvent(store.StoreId, provider, notification.EventId, order.Number, clock.GetUtcNow()));
        await ApplyAsync(notification, order, stock, outbox, metrics, provider, clock, logger, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok();
    }

    private static async Task ApplyAsync(
        PaymentNotification notification,
        Order order,
        IStockLedger stock,
        IOutbox outbox,
        IShopForgeMetrics metrics,
        string provider,
        TimeProvider clock,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var result = notification.Result;
        var applied = result switch
        {
            PaymentResult.Paid => order.ConfirmPayment(clock.GetUtcNow(), notification.PaymentReference),
            _ => order.Cancel(),
        };

        if (!applied)
        {
            // Late or out-of-order events: an order that is already paid or cancelled keeps the state it has, and a
            // payment that arrives after cancellation is a refund case for the store (D-060).
            logger.LogWarning("Order {OrderNumber} is {Status}, so a {Result} event changed nothing.", order.Number, order.Status, result);

            return;
        }

        if (result == PaymentResult.Paid)
        {
            await stock.ConfirmAsync(order.Number, cancellationToken);
            outbox.Enqueue(new PaymentReceived(order.Number, order.Email, order.GrandTotal, order.Currency));
            metrics.PaymentConfirmed(provider);
        }
        else
        {
            await stock.ReleaseAsync(order.Number, cancellationToken);
            outbox.Enqueue(new OrderCancelled(order.Number, order.Email, "The payment was not completed in time."));
            metrics.OrderCancelled("payment_failed");
        }
    }
}

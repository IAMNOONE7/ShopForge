using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Diagnostics;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;

namespace ShopForge.Orders.Payments;

// The one path a payment result travels, whoever brought it: a provider's push, or a sweep that went asking
// because nobody pushed. Two routes to this would be two chances to confirm one payment twice, so there is one
// (D-176). The caller has already put the right store in scope.
internal sealed class PaymentResults(
    DbContext dbContext,
    IStockLedger stock,
    IOutbox outbox,
    IShopForgeMetrics metrics,
    IProviderConnections connections,
    TimeProvider clock,
    ILogger<PaymentResults> logger)
{
    public async Task<PaymentRecord> RecordAsync(string provider, PaymentNotification notification, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // A redelivery, or a sweep seeing a state already recorded, finds its own row and stops here. Two
        // arrivals at the same moment race for the unique index instead: the loser's failure makes the pusher
        // retry, and that retry takes this path.
        if (await dbContext.Set<PaymentEvent>().AnyAsync(
                recorded => recorded.Provider == provider && recorded.EventId == notification.EventId, cancellationToken))
        {
            return PaymentRecord.AlreadySeen;
        }

        var order = await dbContext.Set<Order>().SingleOrDefaultAsync(candidate => candidate.Number == notification.OrderNumber, cancellationToken);

        if (order is null)
        {
            logger.LogWarning("A {Provider} result named order {OrderNumber}, which this store does not have.", provider, notification.OrderNumber);

            return PaymentRecord.NoSuchOrder;
        }

        dbContext.Add(new PaymentEvent(notification.StoreId, provider, notification.EventId, order.Number, clock.GetUtcNow()));
        await RecordAttemptAsync(provider, order.Number, notification.Result, cancellationToken);
        await ApplyAsync(notification, order, provider, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return PaymentRecord.Recorded;
    }

    // The attempt this result is about: the one still running for this provider on this order.
    private async Task RecordAttemptAsync(string provider, string orderNumber, PaymentResult result, CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Set<PaymentAttempt>()
            .Where(candidate => candidate.OrderNumber == orderNumber && candidate.Provider == provider)
            .OrderByDescending(candidate => candidate.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (attempt is null)
        {
            return;
        }

        attempt.Record(
            result switch
            {
                PaymentResult.Paid => PaymentAttemptStatus.Paid,
                PaymentResult.Failed => PaymentAttemptStatus.Failed,
                PaymentResult.Authorized => PaymentAttemptStatus.Authorized,
                _ => PaymentAttemptStatus.Pending,
            },
            clock.GetUtcNow());

        // Which environment this went through, recorded the first time anybody hears back about it: a merchant
        // reconciling money needs to know a paid order was paid in test mode (D-143, D-176).
        if (attempt.Environment is null && await connections.FindAsync(provider, cancellationToken) is { } connection)
        {
            attempt.Went(connection.Environment == ProviderEnvironment.Test ? "test" : "live");
        }
    }

    private async Task ApplyAsync(PaymentNotification notification, Order order, string provider, CancellationToken cancellationToken)
    {
        var result = notification.Result;

        // A payment that has not finished is not a payment that failed. Only a result that says the money will
        // never arrive cancels the order and gives its stock back; everything else leaves the order where it is,
        // including an authorisation, which is held money rather than taken money (D-140).
        if (result is not (PaymentResult.Paid or PaymentResult.Failed))
        {
            logger.LogInformation(
                "Order {OrderNumber} is {Status}; a {Provider} result says {Result}, which changes nothing yet.",
                order.Number,
                order.Status,
                provider,
                result);

            return;
        }

        var applied = result switch
        {
            PaymentResult.Paid => order.ConfirmPayment(clock.GetUtcNow(), notification.PaymentReference),
            _ => order.Cancel(),
        };

        if (!applied)
        {
            // Late or out-of-order results: an order that is already paid or cancelled keeps the state it has, and
            // a payment that arrives after cancellation is a refund case for the store (D-060).
            logger.LogWarning("Order {OrderNumber} is {Status}, so a {Result} result changed nothing.", order.Number, order.Status, result);

            return;
        }

        if (result == PaymentResult.Paid)
        {
            await stock.ConfirmAsync(order.Number, cancellationToken);
            outbox.Enqueue(new PaymentReceived(order.Number, order.Email, order.GrandTotal, order.Currency.Code));
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

internal enum PaymentRecord
{
    Recorded,
    AlreadySeen,
    NoSuchOrder,
}

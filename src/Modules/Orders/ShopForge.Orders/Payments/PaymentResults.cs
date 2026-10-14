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

        var attempt = await AttemptAsync(provider, order.Number, notification.PaymentReference, cancellationToken);

        dbContext.Add(new PaymentEvent(notification.StoreId, provider, notification.EventId, order.Number, clock.GetUtcNow()));
        await RecordAttemptAsync(provider, attempt, notification.Result, cancellationToken);
        var count = await ApplyAsync(notification, order, provider, attempt, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Nothing is counted until it is committed: a writer who lost the race leaves no number behind.
        count?.Invoke();

        return PaymentRecord.Recorded;
    }

    // The attempt this result is about: the one the provider named, where it names attempts the way we stored them
    // (Comgate's transaction), and otherwise the latest for this provider on this order — which is what a provider
    // that names the payment differently from the session it started (Stripe) means.
    private async Task<PaymentAttempt?> AttemptAsync(string provider, string orderNumber, string? reference, CancellationToken cancellationToken)
    {
        var attempts = dbContext.Set<PaymentAttempt>()
            .Where(candidate => candidate.OrderNumber == orderNumber && candidate.Provider == provider);

        return (reference is null ? null : await attempts.SingleOrDefaultAsync(candidate => candidate.Reference == reference, cancellationToken))
            ?? await attempts.OrderByDescending(candidate => candidate.StartedAt).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task RecordAttemptAsync(string provider, PaymentAttempt? attempt, PaymentResult result, CancellationToken cancellationToken)
    {
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
                PaymentResult.Unrecognised => PaymentAttemptStatus.Unrecognised,
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

    private async Task<Action?> ApplyAsync(
        PaymentNotification notification,
        Order order,
        string provider,
        PaymentAttempt? attempt,
        CancellationToken cancellationToken)
    {
        var result = notification.Result;

        // Nobody knows what this status means, so nothing is done on the strength of it: the order waits, the sweep
        // keeps asking, and the count is what tells a person to read what the provider said (D-196).
        if (result == PaymentResult.Unrecognised)
        {
            logger.LogWarning(
                "A {Provider} result for order {OrderNumber} has the status {ProviderStatus}, which ShopForge does not know. Nothing was changed.",
                provider,
                order.Number,
                notification.ProviderStatus);

            return () => metrics.PaymentStatusUnrecognised(provider);
        }

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

            return null;
        }

        var applied = result switch
        {
            PaymentResult.Paid => order.ConfirmPayment(clock.GetUtcNow(), notification.PaymentReference),
            _ => order.Cancel(),
        };

        if (!applied)
        {
            // An order that is already paid or cancelled keeps the state it has. A failure arriving late moves no
            // money; a payment arriving late has moved some, and that is kept rather than dropped (D-193).
            if (result == PaymentResult.Paid)
            {
                return await KeepAsync(notification, order, provider, attempt, cancellationToken);
            }

            logger.LogInformation("Order {OrderNumber} is {Status}, so a {Result} result changed nothing.", order.Number, order.Status, result);

            return null;
        }

        if (result == PaymentResult.Paid)
        {
            await stock.ConfirmAsync(order.Number, cancellationToken);
            outbox.Enqueue(new PaymentReceived(order.Number, order.Email, order.GrandTotal, order.Currency.Code));

            return () => metrics.PaymentConfirmed(provider);
        }

        await stock.ReleaseAsync(order.Number, cancellationToken);
        outbox.Enqueue(new OrderCancelled(order.Number, order.Email, "The payment was not completed in time."));

        return () => metrics.OrderCancelled("payment_failed");
    }

    // Money the order cannot take: it was cancelled before the payment landed, or it was already paid with other
    // money. Either way the provider has it and the store owes it back, so it is written down where a merchant can
    // see it and refund it, rather than logged and forgotten (D-193).
    private async Task<Action?> KeepAsync(
        PaymentNotification notification,
        Order order,
        string provider,
        PaymentAttempt? attempt,
        CancellationToken cancellationToken)
    {
        var reference = notification.PaymentReference;

        // The payment the order already has, told again under another id. It is the same money, not more of it,
        // and refunding it as if it were a second payment would give away the goods.
        if (reference is not null && reference == order.PaymentReference)
        {
            logger.LogInformation("Order {OrderNumber} was already paid by {Reference}; the repeat changes nothing.", order.Number, reference);

            return null;
        }

        if (reference is not null && await dbContext.Set<UnappliedPayment>().AnyAsync(
                kept => kept.Provider == provider && kept.Reference == reference, cancellationToken))
        {
            return null;
        }

        var reason = order.Status == OrderStatus.Cancelled ? UnappliedReason.OrderCancelled : UnappliedReason.OrderAlreadyPaid;

        dbContext.Add(new UnappliedPayment(
            order.StoreId,
            order.Number,
            provider,
            reference,
            attempt?.Amount ?? order.GrandTotal,
            attempt?.Currency ?? order.Currency,
            reason,
            clock.GetUtcNow()));

        logger.LogWarning(
            "A {Provider} payment {Reference} arrived for order {OrderNumber}, which is {Status}. It is kept for the store to refund.",
            provider,
            reference,
            order.Number,
            order.Status);

        return () => metrics.PaymentUnapplied(provider, reason == UnappliedReason.OrderCancelled ? "order_cancelled" : "order_already_paid");
    }
}

internal enum PaymentRecord
{
    Recorded,
    AlreadySeen,
    NoSuchOrder,
}

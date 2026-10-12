using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Invoicing;
using ShopForge.Orders.Persistence;
using ShopForge.Shared.Diagnostics;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Returns;

// Sending goods back: what may still be returned, and what happens to money, stock and paperwork when a parcel
// arrives. The store's refund of a whole order goes through here too, so there is one path (D-098).
internal sealed class OrderReturns(
    DbContext dbContext,
    IStoreContext storeContext,
    ICurrentStoreSettings storeSettings,
    IStockLedger stock,
    Invoices invoices,
    IEnumerable<IPaymentRefunds> paymentRefunds,
    IOutbox outbox,
    IShopForgeMetrics metrics,
    TimeProvider clock,
    ILogger<OrderReturns> logger)
{
    public Task<List<OrderReturn>> OfOrderAsync(string orderNumber, CancellationToken cancellationToken) =>
        dbContext.Set<OrderReturn>()
            .Where(orderReturn => orderReturn.OrderNumber == orderNumber)
            .OrderByDescending(orderReturn => orderReturn.RequestedAt)
            .ToListAsync(cancellationToken);

    // What the customer still has: everything bought, less what is already in a return that has not been refused.
    public async Task<IReadOnlyList<ReturnableLine>> ReturnableAsync(Order order, CancellationToken cancellationToken)
    {
        var held = await HeldQuantitiesAsync(order.Number, onlyReceived: false, cancellationToken);

        return
        [
            .. order.Lines
                .Select(line => new ReturnableLine(
                    line.StoreProductId,
                    line.VariantId,
                    line.ProductName,
                    line.Quantity,
                    line.Quantity - held.GetValueOrDefault(line.VariantId)))
                .Where(line => line.Returnable > 0),
        ];
    }

    public async Task<DateTimeOffset?> ClosesAtAsync(Order order, CancellationToken cancellationToken)
    {
        // The clock starts when the goods left the shop; a pickup order that was never handed to a carrier has only
        // its payment to go by (D-094).
        var handedOver = order.Shipment?.ShippedAt ?? order.PaidAt;

        if (handedOver is null)
        {
            return null;
        }

        var settings = await storeSettings.GetAsync(cancellationToken);

        return handedOver.Value.AddDays(settings.ReturnWindowDays);
    }

    public async Task<ReturnRequestResult> RequestAsync(
        Order order,
        Guid? storeCustomerId,
        IReadOnlyList<RequestedReturnLine> requested,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (order.Status is not (OrderStatus.Paid or OrderStatus.Shipped))
        {
            return new ReturnRequestResult(null, $"An order that is {order.Status} cannot be returned.");
        }

        if (await ClosesAtAsync(order, cancellationToken) is { } closesAt && clock.GetUtcNow() > closesAt)
        {
            return new ReturnRequestResult(null, $"The return window for this order closed on {closesAt:yyyy-MM-dd}.");
        }

        await TakeOrderRowAsync(order, cancellationToken);

        var returnable = await ReturnableAsync(order, cancellationToken);
        var lines = new List<(Guid StoreProductId, Guid VariantId, string Name, int Quantity)>();

        foreach (var line in requested.Where(line => line.Quantity > 0))
        {
            // A form is named when the order holds more than one of the same listing; otherwise the listing is
            // enough, which is what every order placed before variants existed looks like.
            var candidates = returnable.Where(candidate => candidate.StoreProductId == line.StoreProductId).ToList();
            var available = line.VariantId is { } named
                ? candidates.SingleOrDefault(candidate => candidate.VariantId == named)
                : candidates.Count == 1 ? candidates[0] : null;

            if (available is null)
            {
                return new ReturnRequestResult(null, "One of the products is not part of this order, or is already being returned.");
            }

            if (line.Quantity > available.Returnable)
            {
                return new ReturnRequestResult(
                    null,
                    $"You can send back {available.Returnable} × {available.ProductName}, not {line.Quantity}.");
            }

            lines.Add((line.StoreProductId, available.VariantId, available.ProductName, line.Quantity));
        }

        if (lines.Count == 0)
        {
            return new ReturnRequestResult(null, "Choose what you are sending back.");
        }

        var requestedAt = clock.GetUtcNow();
        var number = await Numbers.NextDocumentNumberAsync(dbContext, order.StoreId, NumberSeries.Return, requestedAt.Year, cancellationToken);
        var orderReturn = new OrderReturn(storeContext.StoreId!.Value, number, order.Number, storeCustomerId, reason, requestedAt);

        foreach (var (storeProductId, variantId, name, quantity) in lines)
        {
            orderReturn.AddLine(storeProductId, variantId, name, quantity);
        }

        dbContext.Add(orderReturn);

        return new ReturnRequestResult(orderReturn, null);
    }

    // The parcel is here: the goods go back on the shelf, a credit note records what was taken back, and the money
    // follows through the provider that took it (D-095) — after all of this is committed, by SendRefundAsync, never
    // inside it (D-194).
    public async Task<bool> ReceiveAsync(Order order, OrderReturn orderReturn, CancellationToken cancellationToken)
    {
        await TakeOrderRowAsync(order, cancellationToken);

        var refundedBefore = await HeldQuantitiesAsync(order.Number, onlyReceived: true, cancellationToken);
        var credited = new List<CreditedLine>();
        var amount = 0m;

        foreach (var line in orderReturn.Lines)
        {
            var sold = order.Lines.Single(candidate => candidate.VariantId == line.VariantId);
            var share = ReturnedAmounts.DiscountShare(
                sold.Discount,
                sold.Quantity,
                refundedBefore.GetValueOrDefault(line.VariantId),
                line.Quantity,
                order.Currency);

            amount += (sold.UnitPrice * line.Quantity) - share;
            credited.Add(new CreditedLine(sold.ProductName, line.Quantity, sold.UnitPrice, sold.VatRate, share));
        }

        // The customer keeps nothing, so the delivery they paid for is refunded with the goods (D-096).
        var everythingBack = order.Lines.All(line =>
            refundedBefore.GetValueOrDefault(line.VariantId)
                + orderReturn.Lines.Where(returned => returned.VariantId == line.VariantId).Sum(returned => returned.Quantity)
            >= line.Quantity);

        if (everythingBack)
        {
            amount += order.ShippingCharged;
        }

        // Claiming the row before any money moves is what stops two people receiving the same parcel: the second
        // one finds nothing to claim and is told the return is already done (D-095).
        var claimed = await dbContext.Set<OrderReturn>()
            .Where(candidate => candidate.Id == orderReturn.Id && candidate.Status == ReturnStatus.Accepted)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.Status, ReturnStatus.Received), cancellationToken);

        if (claimed == 0 || !order.RecordRefund(amount))
        {
            return false;
        }

        // Booked now, paid afterwards. A provider that has the money to give back is only asked once the receipt
        // is committed, so a lost answer can no longer roll the receipt back and leave the return waiting to be
        // received — and refunded — a second time (D-194).
        var refunds = paymentRefunds.SingleOrDefault(candidate => candidate.Key == order.PaymentProviderKey);
        var refund = amount <= 0 ? RefundStatus.Refunded
            : refunds is not null && order.PaymentReference is { Length: > 0 } ? RefundStatus.Pending
            : RefundStatus.ByStore;

        orderReturn.Receive(amount, refund, clock.GetUtcNow());

        // The order line says which form was sold, so the goods go back on the shelf they came off (D-135).
        var backToStock = orderReturn.Lines.Select(line => new StockRequest(line.VariantId, line.Quantity)).ToList();

        await stock.ReturnAsync(backToStock, order.Number, cancellationToken);

        await invoices.IssueCreditNoteAsync(order, orderReturn.Id, credited, everythingBack, cancellationToken);

        // Money the store gives back itself, or none at all, is the customer's news now. Money a provider moves is
        // their news once the provider has moved it.
        if (refund != RefundStatus.Pending)
        {
            outbox.Enqueue(new ReturnRefunded(order.Number, order.Email, orderReturn.Number, amount, order.Currency.Code));
            metrics.OrderRefunded(orderReturn.StoreCustomerId is null ? "store" : "return");
        }

        return true;
    }

    // The money, once the books are committed. The return is received, the goods are back and the credit note is
    // issued, so what the provider says decides only where the refund stands. A refusal can be tried again; an
    // answer that never came leaves it pending for somebody to check, because the money may already have moved.
    public async Task<RefundOutcome> SendRefundAsync(Guid returnId, CancellationToken cancellationToken)
    {
        var orderReturn = await dbContext.Set<OrderReturn>().AsNoTracking().SingleAsync(candidate => candidate.Id == returnId, cancellationToken);
        var order = await dbContext.Set<Order>().AsNoTracking().SingleAsync(candidate => candidate.Number == orderReturn.OrderNumber, cancellationToken);
        var refunds = paymentRefunds.SingleOrDefault(candidate => candidate.Key == order.PaymentProviderKey);

        // The gateway this order was paid through is not part of this deployment any more, so nothing here can move
        // the money. That is a refusal like any other, with its reason, rather than an error that leaves it pending.
        if (refunds is null || order.PaymentReference is not { Length: > 0 } reference)
        {
            var unreachable = $"This deployment cannot give money back through {order.PaymentProviderKey}. Refund it there, then close this by hand.";

            await SettleAsync(returnId, RefundStatus.Failed, unreachable, CancellationToken.None);
            metrics.RefundFailed(order.PaymentProviderKey, "refused");

            return new RefundOutcome(RefundStatus.Failed, unreachable);
        }

        try
        {
            await refunds.RefundAsync(
                new RefundRequest(order.Number, reference, orderReturn.RefundedAmount, order.Currency), cancellationToken);
        }
        catch (RefundRefusedException refusal)
        {
            await SettleAsync(returnId, RefundStatus.Failed, refusal.Message, CancellationToken.None);
            metrics.RefundFailed(order.PaymentProviderKey, "refused");

            return new RefundOutcome(RefundStatus.Failed, refusal.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A timeout arrives as a cancellation, and it is this case too: the request went out and nothing came back.
            logger.LogError(
                exception,
                "Refunding return {ReturnNumber} of order {OrderNumber} through {Provider} had no answer; it is left for the store to check.",
                orderReturn.Number,
                order.Number,
                order.PaymentProviderKey);

            var problem = $"The gateway ({order.PaymentProviderKey}) did not say whether the money went back. Check it there before anything else, then close this by hand.";

            await dbContext.Set<OrderReturn>()
                .Where(candidate => candidate.Id == returnId && candidate.Refund == RefundStatus.Pending)
                .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.RefundProblem, problem), CancellationToken.None);
            metrics.RefundFailed(order.PaymentProviderKey, "unanswered");

            return new RefundOutcome(RefundStatus.Pending, problem);
        }

        // Confirmed, so it is written down whatever happens to the request from here: the money has moved.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken.None);

        if (await SettleAsync(returnId, RefundStatus.Refunded, problem: null, CancellationToken.None))
        {
            outbox.Enqueue(new ReturnRefunded(order.Number, order.Email, orderReturn.Number, orderReturn.RefundedAmount, order.Currency.Code));
        }
        else
        {
            logger.LogWarning("Return {ReturnNumber} was refunded through {Provider} after it had been closed by hand.", orderReturn.Number, order.PaymentProviderKey);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        metrics.OrderRefunded(orderReturn.StoreCustomerId is null ? "store" : "return");

        return new RefundOutcome(RefundStatus.Refunded, null);
    }

    // A refused refund is tried again by whoever claims it first, so two presses send the money once.
    public async Task<bool> ClaimAnotherTryAsync(Guid returnId, CancellationToken cancellationToken) =>
        await dbContext.Set<OrderReturn>()
            .Where(candidate => candidate.Id == returnId && candidate.Refund == RefundStatus.Failed)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(candidate => candidate.Refund, RefundStatus.Pending)
                    .SetProperty(candidate => candidate.RefundProblem, (string?)null),
                cancellationToken) == 1;

    // Settled somewhere ShopForge cannot see — refunded at the provider's own portal, or paid back another way —
    // and closed with a note saying how. A refusal or an unanswered refund can be; a settled one cannot.
    public async Task<bool> CloseRefundByHandAsync(Guid returnId, string note, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        return await dbContext.Set<OrderReturn>()
            .Where(candidate => candidate.Id == returnId
                && (candidate.Refund == RefundStatus.Failed || candidate.Refund == RefundStatus.Pending))
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(candidate => candidate.Refund, RefundStatus.ClosedByHand)
                    .SetProperty(candidate => candidate.RefundNote, note)
                    .SetProperty(candidate => candidate.RefundSettledAt, now),
                cancellationToken) == 1;
    }

    // From pending to what the provider said, and only from pending: a refund somebody closed by hand meanwhile
    // keeps that.
    private async Task<bool> SettleAsync(Guid returnId, RefundStatus answer, string? problem, CancellationToken cancellationToken)
    {
        DateTimeOffset? settledAt = answer == RefundStatus.Refunded ? clock.GetUtcNow() : null;

        return await dbContext.Set<OrderReturn>()
            .Where(candidate => candidate.Id == returnId && candidate.Refund == RefundStatus.Pending)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(candidate => candidate.Refund, answer)
                    .SetProperty(candidate => candidate.RefundProblem, problem)
                    .SetProperty(candidate => candidate.RefundSettledAt, settledAt),
                cancellationToken) == 1;
    }

    // Everything here is worked out from the other returns of the same order, so two parcels arriving at the same
    // moment would each compute from what the other had not committed yet — and the delivery, which is refunded
    // only with the last of the goods, would be refunded with neither. Taking the order's row first makes them
    // queue, and the reload is what the second one then reads instead of what it remembered (D-130).
    private async Task TakeOrderRowAsync(Order order, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM orders.orders WHERE id = {0} FOR UPDATE", [order.Id], cancellationToken);
        await dbContext.Entry(order).ReloadAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, int>> HeldQuantitiesAsync(string orderNumber, bool onlyReceived, CancellationToken cancellationToken)
    {
        var lines = await dbContext.Set<OrderReturn>()
            .Where(orderReturn => orderReturn.OrderNumber == orderNumber)
            .Where(orderReturn => onlyReceived
                ? orderReturn.Status == ReturnStatus.Received
                : orderReturn.Status != ReturnStatus.Refused)
            .SelectMany(orderReturn => orderReturn.Lines)
            .GroupBy(line => line.VariantId)
            .Select(group => new { VariantId = group.Key, Quantity = group.Sum(line => line.Quantity) })
            .ToListAsync(cancellationToken);

        return lines.ToDictionary(line => line.VariantId, line => line.Quantity);
    }
}

internal sealed record ReturnableLine(Guid StoreProductId, Guid VariantId, string ProductName, int Bought, int Returnable);

internal sealed record RequestedReturnLine(Guid StoreProductId, Guid? VariantId, int Quantity);

internal sealed record ReturnRequestResult(OrderReturn? Created, string? Problem);

internal sealed record RefundOutcome(RefundStatus Status, string? Problem);

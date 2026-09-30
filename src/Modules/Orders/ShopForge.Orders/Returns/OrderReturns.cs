using Microsoft.EntityFrameworkCore;
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
    TimeProvider clock)
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
    // follows through the provider that took it (D-095).
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
                line.Quantity);

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

        orderReturn.Receive(amount, clock.GetUtcNow());

        // The order line says which form was sold, so the goods go back on the shelf they came off (D-135).
        var backToStock = orderReturn.Lines.Select(line => new StockRequest(line.VariantId, line.Quantity)).ToList();

        await stock.ReturnAsync(backToStock, order.Number, cancellationToken);

        await invoices.IssueCreditNoteAsync(order, orderReturn.Id, credited, everythingBack, cancellationToken);

        var refunds = paymentRefunds.SingleOrDefault(candidate => candidate.Key == order.PaymentProviderKey);

        if (amount > 0 && refunds is not null && order.PaymentReference is { Length: > 0 } reference)
        {
            await refunds.RefundAsync(new RefundRequest(order.Number, reference, amount, order.Currency), cancellationToken);
        }

        outbox.Enqueue(new ReturnRefunded(order.Number, order.Email, orderReturn.Number, amount, order.Currency));
        metrics.OrderRefunded(orderReturn.StoreCustomerId is null ? "store" : "return");

        return true;
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

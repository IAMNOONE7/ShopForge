using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Export;
using ShopForge.Orders.Invoicing;
using ShopForge.Orders.Returns;
using ShopForge.Orders.Shipping;
using ShopForge.Orders.Storefront;
using ShopForge.Shared.Admin;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Diagnostics;
using ShopForge.Shared.Http;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;
using ShopForge.Shared.Shipping;

namespace ShopForge.Orders.Admin;

internal static class AdminOrderEndpoints
{
    public static void MapAdminOrders(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapGet("/orders", GetOrdersAsync);
        storeAdmin.MapGet("/orders/{number}", GetOrderAsync);
        storeAdmin.MapPost("/orders/{number}/payment", ConfirmPaymentAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPost("/orders/{number}/cancel", CancelAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPost("/orders/{number}/shipment", CreateShipmentAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPost("/orders/{number}/refund", RefundAsync).RequireAuthorization(AdminPolicies.StoreManagement).Idempotent();
        storeAdmin.MapGet("/orders/export", ExportAsync)
            .RequireAuthorization(AdminPolicies.StoreManagement)
            .RequireRateLimiting(RateLimits.Expensive);
    }

    // A year of orders is a list somebody has to work through, not one to truncate at two hundred and hope
    // the rest were not wanted (D-179).
    private static async Task<Ok<AdminListResponse<AdminOrderResponse>>> GetOrdersAsync(
        DbContext dbContext,
        int? page,
        int? pageSize,
        string? sort,
        string? q,
        CancellationToken cancellationToken)
    {
        var query = AdminListQuery.Of(page, pageSize, sort, q);
        var orders = dbContext.Set<Order>().AsNoTracking();

        // An order is looked for by its number, by who placed it, or by their name on it — the three things a
        // merchant has in front of them when a customer is on the telephone.
        if (query.Pattern is { } pattern)
        {
            orders = orders.Where(order =>
                EF.Functions.Like(order.Number.ToLower(), pattern)
                || EF.Functions.Like(order.Email.ToLower(), pattern)
                || EF.Functions.Like(order.BillingAddress.FullName.ToLower(), pattern));
        }

        orders = (query.SortKey, query.Descending) switch
        {
            ("placed", false) => orders.OrderBy(order => order.PlacedAt).ThenBy(order => order.Number),
            // There is deliberately no sort by what the order came to: an order's money is worked out from its
            // lines rather than stored, so the database has nothing to order by. It would need a column, and
            // a column that can disagree with the lines it is a sum of is worse than a missing sort (D-179).
            ("placed", true) => orders.OrderByDescending(order => order.PlacedAt).ThenByDescending(order => order.Number),
            ("status", false) => orders.OrderBy(order => order.Status).ThenByDescending(order => order.Number),
            ("status", true) => orders.OrderByDescending(order => order.Status).ThenByDescending(order => order.Number),

            // Newest first is what a merchant opening the screen wants, so it is also what no instruction
            // means. The number breaks the tie in the same direction: two orders placed in the same instant
            // read as one before the other, and the later number is the later order.
            _ => orders.OrderByDescending(order => order.PlacedAt).ThenByDescending(order => order.Number),
        };

        var total = await orders.CountAsync(cancellationToken);
        var wanted = await orders.Skip(query.Skipped).Take(query.Taken).ToListAsync(cancellationToken);

        return TypedResults.Ok(new AdminListResponse<AdminOrderResponse>(
            [.. wanted.Select(AdminOrderResponse.From)], total, query.Page, query.PageSize));
    }

    private static async Task<Results<Ok<AdminOrderDetailResponse>, NotFound>> GetOrderAsync(
        string number,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>().AsNoTracking().SingleOrDefaultAsync(order => order.Number == number, cancellationToken);

        return order is null ? TypedResults.NotFound() : TypedResults.Ok(await DetailAsync(dbContext, order, cancellationToken));
    }

    // Every answer about an order lists the documents it has, so the admin never has to reload to see a new one.
    // A year of orders for the accountant, or a month of them. Without a window it is the most recent of
    // whatever there is, because a file built in memory has a size past which it is an outage (D-185).
    private static async Task<FileStreamHttpResult> ExportAsync(
        DbContext dbContext,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken) =>
        TypedResults.File(
            await OrderExport.WriteAsync(dbContext, from, to, cancellationToken),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"shopforge-orders-{DateTime.UtcNow:yyyy-MM-dd}.xlsx");

    private static async Task<AdminOrderDetailResponse> DetailAsync(DbContext dbContext, Order order, CancellationToken cancellationToken) =>
        AdminOrderDetailResponse.From(
            order,
            await Documents.OfAsync(dbContext, order.Number, cancellationToken),
            await AttemptsAsync(dbContext, order.Number, cancellationToken));

    // Every try at paying this order, newest first: what the gateway called it, what became of it, and which
    // of the gateway's environments it went through. No secret is readable from any of it — an attempt holds a
    // transaction id and a redirect the shopper was already sent to, and nothing else (D-139, D-176).
    private static async Task<List<AdminPaymentAttemptResponse>> AttemptsAsync(
        DbContext dbContext, string orderNumber, CancellationToken cancellationToken) =>
        await dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .Where(attempt => attempt.OrderNumber == orderNumber)
            .OrderByDescending(attempt => attempt.StartedAt)
            .Select(attempt => new AdminPaymentAttemptResponse(
                attempt.Provider,
                attempt.Reference,
                attempt.Status.ToString(),
                attempt.Environment,
                attempt.Amount,
                attempt.Currency,
                attempt.StartedAt,
                attempt.ChangedAt))
            .ToListAsync(cancellationToken);

    // Payment for the manual methods is confirmed by hand; the reserved stock leaves the warehouse at that moment.
    private static Task<Results<Ok<AdminOrderDetailResponse>, NotFound, ProblemHttpResult>> ConfirmPaymentAsync(
        string number,
        DbContext dbContext,
        IStockLedger stock,
        IOutbox outbox,
        IShopForgeMetrics metrics,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        ChangeAsync(
            number,
            dbContext,
            order => order.ConfirmPayment(clock.GetUtcNow()),
            async (order, token) =>
            {
                await stock.ConfirmAsync(order.Number, token);
                await SettleAttemptAsync(dbContext, order, PaymentAttemptStatus.Paid, clock, token);
                outbox.Enqueue(new PaymentReceived(order.Number, order.Email, order.GrandTotal, order.Currency));
            },
            order => metrics.PaymentConfirmed(order.PaymentMethodCode),
            "The order is not awaiting payment",
            cancellationToken);

    private static Task<Results<Ok<AdminOrderDetailResponse>, NotFound, ProblemHttpResult>> CancelAsync(
        string number,
        DbContext dbContext,
        IStockLedger stock,
        IOutbox outbox,
        IShopForgeMetrics metrics,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        ChangeAsync(
            number,
            dbContext,
            order => order.Cancel(),
            async (order, token) =>
            {
                await stock.ReleaseAsync(order.Number, token);
                await SettleAttemptAsync(dbContext, order, PaymentAttemptStatus.Failed, clock, token);
                outbox.Enqueue(new OrderCancelled(order.Number, order.Email, "The store cancelled the order."));
            },
            _ => metrics.OrderCancelled("admin"),
            "Only an order that is awaiting payment can be cancelled",
            cancellationToken);

    // The store taking everything back is a return it makes itself and receives at once, so money, goods and
    // documents follow the one path a customer's return follows (D-098).
    private static async Task<Results<Ok<AdminOrderDetailResponse>, NotFound, ProblemHttpResult>> RefundAsync(
        string number,
        DbContext dbContext,
        OrderReturns returns,
        IAuditLog audit,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await dbContext.Set<Order>().SingleOrDefaultAsync(candidate => candidate.Number == number, cancellationToken);

        if (order is null)
        {
            return TypedResults.NotFound();
        }

        var outstanding = await returns.ReturnableAsync(order, cancellationToken);
        var refund = await returns.RequestAsync(
            order,
            storeCustomerId: null,
            [.. outstanding.Select(line => new RequestedReturnLine(line.StoreProductId, line.VariantId, line.Returnable))],
            "Refunded by the store.",
            cancellationToken);

        if (refund.Created is not { } orderReturn)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "There is nothing left to refund on this order",
                detail: refund.Problem);
        }

        orderReturn.Accept(clock.GetUtcNow());

        // Receiving claims the return's row, so the row has to exist before the parcel can be marked as arrived.
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!await returns.ReceiveAsync(order, orderReturn, cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Only a paid order can be refunded",
                detail: $"The order is {order.Status}.");
        }

        audit.Record("order.refunded", order.Number, new { orderReturn.Number, amount = orderReturn.RefundedAmount });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(await DetailAsync(dbContext, order, cancellationToken));
    }

    // The parcel is handed to the carrier by the store; the provider turns that into a tracking number (D-063).
    private static async Task<Results<Ok<AdminOrderDetailResponse>, ValidationProblem, NotFound, ProblemHttpResult>> CreateShipmentAsync(
        string number,
        ShipmentCreationRequest request,
        DbContext dbContext,
        IEnumerable<IShippingProvider> shippingProviders,
        IOutbox outbox,
        IShopForgeMetrics metrics,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Set<Order>().SingleOrDefaultAsync(candidate => candidate.Number == number, cancellationToken);

        if (order is null)
        {
            return TypedResults.NotFound();
        }

        // The order's own carrier, not the method's today: renaming or deactivating a method after the fact must
        // not change how an order already placed is shipped, and looking it up again did exactly that (D-081).
        var provider = shippingProviders.SingleOrDefault(candidate => candidate.Key == order.ShippingProviderKey);

        if (provider is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The shipping provider of this order is not available",
                detail: $"Order {order.Number} was placed with a method this deployment does not have.");
        }

        var errors = new RequestErrors().Check(
            provider.Key != StoreShippingProvider.ProviderKey || !string.IsNullOrWhiteSpace(request.TrackingNumber),
            "trackingNumber",
            "A tracking number is required for a shipment the store hands over itself.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var details = await provider.CreateShipmentAsync(
            new ShipmentRequest(
                order.Number,
                order.ShippingMethodName,
                order.ShippingAddress.FullName,
                order.ShippingAddress.Line1,
                order.ShippingAddress.City,
                order.ShippingAddress.PostalCode,
                order.ShippingAddress.Country,
                order.PickupPointCode,
                request.TrackingNumber?.Trim()),
            cancellationToken);

        if (!order.Ship(details, clock.GetUtcNow()))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Only a paid order can be shipped",
                detail: $"The order is {order.Status}.");
        }

        outbox.Enqueue(new ShipmentCreated(order.Number, order.Email, details.Carrier, details.TrackingNumber, order.PickupPointName));
        await dbContext.SaveChangesAsync(cancellationToken);
        metrics.ShipmentCreated();

        return TypedResults.Ok(await DetailAsync(dbContext, order, cancellationToken));
    }

    // A store that takes the money itself still has an attempt to close, so "how was this order paid for" has
    // one answer rather than one per kind of method.
    private static async Task SettleAttemptAsync(
        DbContext dbContext,
        Order order,
        PaymentAttemptStatus status,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var attempt = await dbContext.Set<PaymentAttempt>()
            .Where(candidate => candidate.OrderNumber == order.Number)
            .OrderByDescending(candidate => candidate.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        attempt?.Record(status, clock.GetUtcNow());
    }

    private static async Task<Results<Ok<AdminOrderDetailResponse>, NotFound, ProblemHttpResult>> ChangeAsync(
        string number,
        DbContext dbContext,
        Func<Order, bool> change,
        Func<Order, CancellationToken, Task> moveStock,
        Action<Order> count,
        string rejection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await dbContext.Set<Order>().SingleOrDefaultAsync(order => order.Number == number, cancellationToken);

        if (order is null)
        {
            return TypedResults.NotFound();
        }

        if (!change(order))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: rejection, detail: $"The order is {order.Status}.");
        }

        await moveStock(order, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Nothing is counted until it is committed: a writer who lost the row leaves no work behind, and a metric
        // is the one effect a rolled-back transaction cannot take with it.
        count(order);

        return TypedResults.Ok(await DetailAsync(dbContext, order, cancellationToken));
    }
}

internal sealed record AdminOrderResponse(
    string Number,
    DateTimeOffset PlacedAt,
    string Status,
    string Email,
    bool HasAccount,
    decimal GrandTotal,
    int Items)
{
    public static AdminOrderResponse From(Order order) => new(
        order.Number,
        order.PlacedAt,
        order.Status.ToString(),
        order.Email,
        order.StoreCustomerId is not null,
        order.GrandTotal,
        order.Lines.Sum(line => line.Quantity));
}

internal sealed record AdminOrderDetailResponse(
    string Number,
    DateTimeOffset PlacedAt,
    string Status,
    string Email,
    string? Phone,
    string Currency,
    string PaymentMethod,
    string ShippingMethod,
    string? Carrier,
    decimal ShippingPrice,
    decimal ItemsTotal,
    decimal VatTotal,
    decimal GrandTotal,
    OrderDiscountResponse? Discount,
    AdminAddressResponse BillingAddress,
    AdminAddressResponse ShippingAddress,
    string? PickupPoint,
    AdminShipmentResponse? Shipment,
    // What the order itself says was paid, beside every try at paying it. A merchant chasing money that never
    // arrived needs both: the order's own answer, and the story of how it got there (D-176).
    string? PaymentReference,
    DateTimeOffset? PaidAt,
    List<AdminPaymentAttemptResponse> PaymentAttempts,
    List<DocumentResponse> Documents,
    List<AdminOrderLineResponse> Lines)
{
    public static AdminOrderDetailResponse From(
        Order order, List<DocumentResponse> documents, List<AdminPaymentAttemptResponse> attempts) => new(
        order.Number,
        order.PlacedAt,
        order.Status.ToString(),
        order.Email,
        order.Phone,
        order.Currency,
        order.PaymentMethodName,
        order.ShippingMethodName,
        order.ShippingProviderKey,
        order.ShippingCharged,
        order.ItemsTotal,
        order.VatTotal,
        order.GrandTotal,
        order.DiscountCode is null ? null : new OrderDiscountResponse(order.DiscountCode, order.DiscountName!, order.DiscountTotal),
        AdminAddressResponse.From(order.BillingAddress),
        AdminAddressResponse.From(order.ShippingAddress),
        order.PickupPointName is null ? null : $"{order.PickupPointName}, {order.PickupPointAddress!.Line1}, {order.PickupPointAddress.City}",
        order.Shipment is null
            ? null
            : new AdminShipmentResponse(order.Shipment.Carrier, order.Shipment.TrackingNumber, order.Shipment.TrackingUrl, order.Shipment.ShippedAt),
        order.PaymentReference,
        order.PaidAt,
        attempts,
        documents,
        [.. order.Lines.Select(line => new AdminOrderLineResponse(line.ProductName, line.UnitPrice, line.VatRate, line.Quantity, line.LineTotal))]);
}

internal sealed record AdminAddressResponse(string FullName, string Line1, string? Line2, string City, string PostalCode, string Country)
{
    public static AdminAddressResponse From(Address address) =>
        new(address.FullName, address.Line1, address.Line2, address.City, address.PostalCode, address.Country);
}

internal sealed record AdminOrderLineResponse(string ProductName, decimal UnitPrice, decimal VatRate, int Quantity, decimal LineTotal);

// One try at paying, as the admin sees it. Everything here is already known to whoever can see the order.
internal sealed record AdminPaymentAttemptResponse(
    string Provider,
    string? Reference,
    string Status,
    string? Environment,
    decimal Amount,
    string Currency,
    DateTimeOffset StartedAt,
    DateTimeOffset ChangedAt);

internal sealed record ShipmentCreationRequest(string? TrackingNumber);

internal sealed record AdminShipmentResponse(string Carrier, string TrackingNumber, string? TrackingUrl, DateTimeOffset ShippedAt);

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Invoicing;
using ShopForge.Orders.Shipping;
using ShopForge.Orders.Storefront;
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
        storeAdmin.MapPost("/orders/{number}/refund", RefundAsync).RequireAuthorization(AdminPolicies.StoreManagement);
    }

    private static async Task<Ok<List<AdminOrderResponse>>> GetOrdersAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var orders = await dbContext.Set<Order>()
            .AsNoTracking()
            .OrderByDescending(order => order.PlacedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(orders.Select(AdminOrderResponse.From).ToList());
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
    private static async Task<AdminOrderDetailResponse> DetailAsync(DbContext dbContext, Order order, CancellationToken cancellationToken) =>
        AdminOrderDetailResponse.From(order, await Documents.OfAsync(dbContext, order.Number, cancellationToken));

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
                outbox.Enqueue(new PaymentReceived(order.Number, order.Email, order.GrandTotal, order.Currency));
                metrics.PaymentConfirmed(order.PaymentMethodCode);
            },
            "The order is not awaiting payment",
            cancellationToken);

    private static Task<Results<Ok<AdminOrderDetailResponse>, NotFound, ProblemHttpResult>> CancelAsync(
        string number,
        DbContext dbContext,
        IStockLedger stock,
        IOutbox outbox,
        IShopForgeMetrics metrics,
        CancellationToken cancellationToken) =>
        ChangeAsync(
            number,
            dbContext,
            order => order.Cancel(),
            async (order, token) =>
            {
                await stock.ReleaseAsync(order.Number, token);
                outbox.Enqueue(new OrderCancelled(order.Number, order.Email, "The store cancelled the order."));
                metrics.OrderCancelled("admin");
            },
            "Only an order that is awaiting payment can be cancelled",
            cancellationToken);

    // Money and goods go back together, and the credit note records it (D-081).
    private static async Task<Results<Ok<AdminOrderDetailResponse>, NotFound, ProblemHttpResult>> RefundAsync(
        string number,
        DbContext dbContext,
        IStockLedger stock,
        ISellableProducts products,
        Invoices invoices,
        IEnumerable<IPaymentRefunds> paymentRefunds,
        IOutbox outbox,
        IShopForgeMetrics metrics,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await dbContext.Set<Order>().SingleOrDefaultAsync(candidate => candidate.Number == number, cancellationToken);

        if (order is null)
        {
            return TypedResults.NotFound();
        }

        if (!order.Refund())
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Only a paid order can be refunded",
                detail: $"The order is {order.Status}.");
        }

        var refunds = paymentRefunds.SingleOrDefault(candidate => candidate.Key == order.PaymentProviderKey);

        if (refunds is not null && order.PaymentReference is { Length: > 0 } reference)
        {
            await refunds.RefundAsync(new RefundRequest(order.Number, reference, order.GrandTotal, order.Currency), cancellationToken);
        }

        var sold = await products.FindAsync([.. order.Lines.Select(line => line.StoreProductId)], cancellationToken);
        var returned = order.Lines
            .Join(sold, line => line.StoreProductId, product => product.StoreProductId, (line, product) => new StockRequest(product.ProductId, line.Quantity))
            .ToList();

        if (returned.Count > 0)
        {
            await stock.ReturnAsync(returned, order.Number, cancellationToken);
        }

        await invoices.IssueAsync(order, InvoiceKind.CreditNote, cancellationToken);
        outbox.Enqueue(new OrderCancelled(order.Number, order.Email, "The order was refunded."));
        metrics.OrderCancelled("refund");
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

        var method = await dbContext.Set<ShippingMethod>()
            .SingleOrDefaultAsync(candidate => candidate.Code == order.ShippingMethodCode, cancellationToken);
        var provider = method is null ? null : shippingProviders.SingleOrDefault(candidate => candidate.Key == method.ProviderKey);

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
        metrics.ShipmentCreated();
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await DetailAsync(dbContext, order, cancellationToken));
    }

    private static async Task<Results<Ok<AdminOrderDetailResponse>, NotFound, ProblemHttpResult>> ChangeAsync(
        string number,
        DbContext dbContext,
        Func<Order, bool> change,
        Func<Order, CancellationToken, Task> moveStock,
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
    string Currency,
    string PaymentMethod,
    string ShippingMethod,
    decimal ShippingPrice,
    decimal ItemsTotal,
    decimal VatTotal,
    decimal GrandTotal,
    OrderDiscountResponse? Discount,
    AdminAddressResponse BillingAddress,
    AdminAddressResponse ShippingAddress,
    string? PickupPoint,
    AdminShipmentResponse? Shipment,
    List<DocumentResponse> Documents,
    List<AdminOrderLineResponse> Lines)
{
    public static AdminOrderDetailResponse From(Order order, List<DocumentResponse> documents) => new(
        order.Number,
        order.PlacedAt,
        order.Status.ToString(),
        order.Email,
        order.Currency,
        order.PaymentMethodName,
        order.ShippingMethodName,
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
        documents,
        [.. order.Lines.Select(line => new AdminOrderLineResponse(line.ProductName, line.UnitPrice, line.VatRate, line.Quantity, line.LineTotal))]);
}

internal sealed record AdminAddressResponse(string FullName, string Line1, string? Line2, string City, string PostalCode, string Country)
{
    public static AdminAddressResponse From(Address address) =>
        new(address.FullName, address.Line1, address.Line2, address.City, address.PostalCode, address.Country);
}

internal sealed record AdminOrderLineResponse(string ProductName, decimal UnitPrice, decimal VatRate, int Quantity, decimal LineTotal);

internal sealed record ShipmentCreationRequest(string? TrackingNumber);

internal sealed record AdminShipmentResponse(string Carrier, string TrackingNumber, string? TrackingUrl, DateTimeOffset ShippedAt);

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Security;

namespace ShopForge.Orders.Admin;

internal static class AdminOrderEndpoints
{
    public static void MapAdminOrders(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapGet("/orders", GetOrdersAsync);
        storeAdmin.MapGet("/orders/{number}", GetOrderAsync);
        storeAdmin.MapPost("/orders/{number}/payment", ConfirmPaymentAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPost("/orders/{number}/cancel", CancelAsync).RequireAuthorization(AdminPolicies.StoreManagement);
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

        return order is null ? TypedResults.NotFound() : TypedResults.Ok(AdminOrderDetailResponse.From(order));
    }

    // Payment for the manual methods is confirmed by hand; the reserved stock leaves the warehouse at that moment.
    private static Task<Results<Ok<AdminOrderDetailResponse>, NotFound, ProblemHttpResult>> ConfirmPaymentAsync(
        string number,
        DbContext dbContext,
        IStockLedger stock,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        ChangeAsync(
            number,
            dbContext,
            order => order.ConfirmPayment(clock.GetUtcNow()),
            (order, token) => stock.ConfirmAsync(order.Number, token),
            "The order is not awaiting payment",
            cancellationToken);

    private static Task<Results<Ok<AdminOrderDetailResponse>, NotFound, ProblemHttpResult>> CancelAsync(
        string number,
        DbContext dbContext,
        IStockLedger stock,
        CancellationToken cancellationToken) =>
        ChangeAsync(
            number,
            dbContext,
            order => order.Cancel(),
            (order, token) => stock.ReleaseAsync(order.Number, token),
            "Only an order that is awaiting payment can be cancelled",
            cancellationToken);

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

        return TypedResults.Ok(AdminOrderDetailResponse.From(order));
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
    AdminAddressResponse BillingAddress,
    AdminAddressResponse ShippingAddress,
    List<AdminOrderLineResponse> Lines)
{
    public static AdminOrderDetailResponse From(Order order) => new(
        order.Number,
        order.PlacedAt,
        order.Status.ToString(),
        order.Email,
        order.Currency,
        order.PaymentMethodName,
        order.ShippingMethodName,
        order.ShippingPrice,
        order.ItemsTotal,
        order.VatTotal,
        order.GrandTotal,
        AdminAddressResponse.From(order.BillingAddress),
        AdminAddressResponse.From(order.ShippingAddress),
        [.. order.Lines.Select(line => new AdminOrderLineResponse(line.ProductName, line.UnitPrice, line.VatRate, line.Quantity, line.LineTotal))]);
}

internal sealed record AdminAddressResponse(string FullName, string Line1, string? Line2, string City, string PostalCode, string Country)
{
    public static AdminAddressResponse From(Address address) =>
        new(address.FullName, address.Line1, address.Line2, address.City, address.PostalCode, address.Country);
}

internal sealed record AdminOrderLineResponse(string ProductName, decimal UnitPrice, decimal VatRate, int Quantity, decimal LineTotal);

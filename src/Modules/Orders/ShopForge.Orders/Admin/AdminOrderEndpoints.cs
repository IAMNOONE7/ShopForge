using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;

namespace ShopForge.Orders.Admin;

internal static class AdminOrderEndpoints
{
    public static void MapAdminOrders(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapGet("/orders", GetOrdersAsync);
        storeAdmin.MapGet("/orders/{number}", GetOrderAsync);
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
}

internal sealed record AdminOrderResponse(string Number, DateTimeOffset PlacedAt, string Status, string Email, decimal GrandTotal, int Items)
{
    public static AdminOrderResponse From(Order order) =>
        new(order.Number, order.PlacedAt, order.Status.ToString(), order.Email, order.GrandTotal, order.Lines.Sum(line => line.Quantity));
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

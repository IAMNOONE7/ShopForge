using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Security;

namespace ShopForge.Orders.Storefront;

internal static class CustomerOrderEndpoints
{
    public static void MapCustomerOrders(this IEndpointRouteBuilder storefront)
    {
        var orders = storefront.MapGroup("/account/orders").RequireAuthorization(CustomerPolicies.Customer);

        orders.MapGet("/", GetOrdersAsync);
        orders.MapGet("/{number}", GetOrderAsync);
    }

    private static async Task<Results<Ok<List<CustomerOrderResponse>>, UnauthorizedHttpResult>> GetOrdersAsync(
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindStoreCustomerIdAsync(cancellationToken) is not { } storeCustomerId)
        {
            return TypedResults.Unauthorized();
        }

        var orders = await dbContext.Set<Order>()
            .AsNoTracking()
            .Where(order => order.StoreCustomerId == storeCustomerId)
            .OrderByDescending(order => order.PlacedAt)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(orders.Select(CustomerOrderResponse.From).ToList());
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound, UnauthorizedHttpResult>> GetOrderAsync(
        string number,
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindStoreCustomerIdAsync(cancellationToken) is not { } storeCustomerId)
        {
            return TypedResults.Unauthorized();
        }

        var order = await dbContext.Set<Order>()
            .AsNoTracking()
            .SingleOrDefaultAsync(order => order.Number == number && order.StoreCustomerId == storeCustomerId, cancellationToken);

        return order is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(OrderResponse.From(order, await Documents.OfAsync(dbContext, order.Number, cancellationToken)));
    }
}

internal sealed record CustomerOrderResponse(string Number, DateTimeOffset PlacedAt, string Status, decimal GrandTotal, int Items)
{
    public static CustomerOrderResponse From(Order order) =>
        new(order.Number, order.PlacedAt, order.Status.ToString(), order.GrandTotal, order.Lines.Sum(line => line.Quantity));
}

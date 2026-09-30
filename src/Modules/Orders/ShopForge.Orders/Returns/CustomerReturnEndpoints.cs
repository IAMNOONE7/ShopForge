using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;

namespace ShopForge.Orders.Returns;

// Sending something back is part of the customer's own order, so it hangs off the order they are looking at.
internal static class CustomerReturnEndpoints
{
    public static void MapCustomerReturns(this IEndpointRouteBuilder storefront)
    {
        var returns = storefront.MapGroup("/account/orders/{number}/returns").RequireAuthorization(CustomerPolicies.Customer);

        returns.MapGet("/", GetReturnsAsync);
        returns.MapPost("/", RequestReturnAsync).Idempotent();
    }

    private static async Task<Results<Ok<CustomerReturnsResponse>, NotFound, UnauthorizedHttpResult>> GetReturnsAsync(
        string number,
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        OrderReturns returns,
        CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindAsync(cancellationToken) is not { StoreCustomerId: var storeCustomerId })
        {
            return TypedResults.Unauthorized();
        }

        if (await FindOrderAsync(dbContext, number, storeCustomerId, cancellationToken) is not { } order)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await ReturnsOfAsync(order, returns, cancellationToken));
    }

    private static async Task<Results<Ok<CustomerReturnsResponse>, ValidationProblem, NotFound, UnauthorizedHttpResult, ProblemHttpResult>>
        RequestReturnAsync(
            string number,
            ReturnRequest request,
            DbContext dbContext,
            ICurrentCustomer currentCustomer,
            OrderReturns returns,
            CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindAsync(cancellationToken) is not { StoreCustomerId: var storeCustomerId })
        {
            return TypedResults.Unauthorized();
        }

        if (await FindOrderAsync(dbContext, number, storeCustomerId, cancellationToken) is not { } order)
        {
            return TypedResults.NotFound();
        }

        // The request holds the order's row while it works out what is left to send back, so two of them cannot
        // both book the last item (D-130). A row held outside a transaction is a row let go immediately.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var result = await returns.RequestAsync(
            order,
            storeCustomerId,
            [.. request.Lines.Select(line => new RequestedReturnLine(line.StoreProductId, line.VariantId, line.Quantity))],
            request.Reason?.Trim() is { Length: > 0 } reason ? reason[..Math.Min(reason.Length, OrderReturn.MaxReasonLength)] : null,
            cancellationToken);

        if (result.Problem is { } problem)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: problem);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(await ReturnsOfAsync(order, returns, cancellationToken));
    }

    private static async Task<CustomerReturnsResponse> ReturnsOfAsync(Order order, OrderReturns returns, CancellationToken cancellationToken) =>
        new(
            await returns.ClosesAtAsync(order, cancellationToken),
            [.. (await returns.ReturnableAsync(order, cancellationToken)).Select(line => new ReturnableLineResponse(
                line.StoreProductId,
                line.VariantId,
                line.ProductName,
                line.Returnable))],
            [.. (await returns.OfOrderAsync(order.Number, cancellationToken)).Select(CustomerReturnResponse.From)]);

    private static Task<Order?> FindOrderAsync(DbContext dbContext, string number, Guid storeCustomerId, CancellationToken cancellationToken) =>
        dbContext.Set<Order>()
            .SingleOrDefaultAsync(order => order.Number == number && order.StoreCustomerId == storeCustomerId, cancellationToken);
}

internal sealed record ReturnRequest(List<ReturnRequestLine> Lines, string? Reason);

internal sealed record ReturnRequestLine(Guid StoreProductId, Guid? VariantId, int Quantity);

internal sealed record CustomerReturnsResponse(
    DateTimeOffset? ClosesAt,
    List<ReturnableLineResponse> Returnable,
    List<CustomerReturnResponse> Returns);

internal sealed record ReturnableLineResponse(Guid StoreProductId, Guid VariantId, string ProductName, int Quantity);

internal sealed record CustomerReturnResponse(
    string Number,
    string Status,
    DateTimeOffset RequestedAt,
    decimal RefundedAmount,
    List<ReturnLineResponse> Lines)
{
    public static CustomerReturnResponse From(OrderReturn orderReturn) => new(
        orderReturn.Number,
        orderReturn.Status.ToString(),
        orderReturn.RequestedAt,
        orderReturn.RefundedAmount,
        [.. orderReturn.Lines.Select(line => new ReturnLineResponse(line.ProductName, line.Quantity))]);
}

internal sealed record ReturnLineResponse(string ProductName, int Quantity);

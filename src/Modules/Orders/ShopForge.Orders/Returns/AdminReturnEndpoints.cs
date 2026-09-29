using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Http;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Security;

namespace ShopForge.Orders.Returns;

internal static class AdminReturnEndpoints
{
    public static void MapAdminReturns(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapGet("/returns", GetReturnsAsync);
        storeAdmin.MapPost("/returns/{returnId:guid}/accept", AcceptAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPost("/returns/{returnId:guid}/refuse", RefuseAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPost("/returns/{returnId:guid}/receive", ReceiveAsync).RequireAuthorization(AdminPolicies.StoreManagement).Idempotent();
    }

    private static async Task<Ok<List<AdminReturnResponse>>> GetReturnsAsync(
        string? status,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var wanted = Enum.TryParse<ReturnStatus>(status, ignoreCase: true, out var parsed) ? parsed : (ReturnStatus?)null;

        var returns = await dbContext.Set<OrderReturn>()
            .AsNoTracking()
            .Where(orderReturn => wanted == null || orderReturn.Status == wanted)
            .OrderByDescending(orderReturn => orderReturn.RequestedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(returns.Select(AdminReturnResponse.From).ToList());
    }

    private static Task<Results<Ok<AdminReturnResponse>, NotFound, ProblemHttpResult>> AcceptAsync(
        Guid returnId,
        DbContext dbContext,
        IOutbox outbox,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        DecideAsync(returnId, dbContext, outbox, accepted: true, orderReturn => orderReturn.Accept(clock.GetUtcNow()), cancellationToken);

    private static Task<Results<Ok<AdminReturnResponse>, NotFound, ProblemHttpResult>> RefuseAsync(
        Guid returnId,
        DbContext dbContext,
        IOutbox outbox,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        DecideAsync(returnId, dbContext, outbox, accepted: false, orderReturn => orderReturn.Refuse(clock.GetUtcNow()), cancellationToken);

    private static async Task<Results<Ok<AdminReturnResponse>, NotFound, ProblemHttpResult>> DecideAsync(
        Guid returnId,
        DbContext dbContext,
        IOutbox outbox,
        bool accepted,
        Func<OrderReturn, bool> decide,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Set<OrderReturn>().SingleOrDefaultAsync(candidate => candidate.Id == returnId, cancellationToken) is not { } orderReturn)
        {
            return TypedResults.NotFound();
        }

        if (!decide(orderReturn))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This return has already been dealt with",
                detail: $"The return is {orderReturn.Status}.");
        }

        var order = await OrderOfAsync(dbContext, orderReturn, cancellationToken);

        outbox.Enqueue(new ReturnDecided(order.Number, order.Email, orderReturn.Number, accepted));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(AdminReturnResponse.From(orderReturn));
    }

    private static async Task<Results<Ok<AdminReturnResponse>, NotFound, ProblemHttpResult>> ReceiveAsync(
        Guid returnId,
        DbContext dbContext,
        OrderReturns returns,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (await dbContext.Set<OrderReturn>().SingleOrDefaultAsync(candidate => candidate.Id == returnId, cancellationToken) is not { } orderReturn)
        {
            return TypedResults.NotFound();
        }

        var order = await OrderOfAsync(dbContext, orderReturn, cancellationToken);

        if (!await returns.ReceiveAsync(order, orderReturn, cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Only an accepted return of a paid order can be received",
                detail: $"The return is {orderReturn.Status} and the order is {order.Status}.");
        }

        audit.Record("return.received", orderReturn.Number, new { amount = orderReturn.RefundedAmount });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(AdminReturnResponse.From(orderReturn));
    }

    private static Task<Order> OrderOfAsync(DbContext dbContext, OrderReturn orderReturn, CancellationToken cancellationToken) =>
        dbContext.Set<Order>().SingleAsync(order => order.Number == orderReturn.OrderNumber, cancellationToken);
}

internal sealed record AdminReturnResponse(
    Guid Id,
    string Number,
    string OrderNumber,
    string Status,
    DateTimeOffset RequestedAt,
    decimal RefundedAmount,
    string? Reason,
    List<ReturnLineResponse> Lines)
{
    public static AdminReturnResponse From(OrderReturn orderReturn) => new(
        orderReturn.Id,
        orderReturn.Number,
        orderReturn.OrderNumber,
        orderReturn.Status.ToString(),
        orderReturn.RequestedAt,
        orderReturn.RefundedAmount,
        orderReturn.Reason,
        [.. orderReturn.Lines.Select(line => new ReturnLineResponse(line.ProductName, line.Quantity))]);
}

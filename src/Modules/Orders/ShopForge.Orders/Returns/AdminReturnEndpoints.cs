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
        storeAdmin.MapGet("/returns/{returnId:guid}", GetReturnAsync);
        storeAdmin.MapPost("/returns/{returnId:guid}/accept", AcceptAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPost("/returns/{returnId:guid}/refuse", RefuseAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPost("/returns/{returnId:guid}/receive", ReceiveAsync).RequireAuthorization(AdminPolicies.StoreManagement).Idempotent();
        storeAdmin.MapPost("/returns/{returnId:guid}/refund", RefundAgainAsync).RequireAuthorization(AdminPolicies.StoreManagement).Idempotent();
        storeAdmin.MapPost("/returns/{returnId:guid}/refund/close", CloseRefundAsync)
            .RequireAuthorization(AdminPolicies.StoreManagement)
            .Idempotent();
    }

    // By where the return is, or by where its money is: `?refund=Failed` is the list of customers still owed.
    private static async Task<Ok<List<AdminReturnResponse>>> GetReturnsAsync(
        string? status,
        string? refund,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var wanted = Enum.TryParse<ReturnStatus>(status, ignoreCase: true, out var parsed) ? parsed : (ReturnStatus?)null;
        var owed = Enum.TryParse<RefundStatus>(refund, ignoreCase: true, out var money) ? money : (RefundStatus?)null;

        var returns = await dbContext.Set<OrderReturn>()
            .AsNoTracking()
            .Where(orderReturn => wanted == null || orderReturn.Status == wanted)
            .Where(orderReturn => owed == null || orderReturn.Refund == owed)
            .OrderByDescending(orderReturn => orderReturn.RequestedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(returns.Select(AdminReturnResponse.From).ToList());
    }

    // One return by its id, so a screen can be linked to and reloaded without walking the list for it (D-204).
    private static async Task<Results<Ok<AdminReturnResponse>, NotFound>> GetReturnAsync(
        Guid returnId,
        DbContext dbContext,
        CancellationToken cancellationToken) =>
        await dbContext.Set<OrderReturn>().AsNoTracking().SingleOrDefaultAsync(orderReturn => orderReturn.Id == returnId, cancellationToken) is { } found
            ? TypedResults.Ok(AdminReturnResponse.From(found))
            : TypedResults.NotFound();

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

        // The parcel is booked whatever the provider says next, so this answers with the return as it now stands —
        // refunded, refused with the reason, or waiting for an answer that never came (D-194).
        if (orderReturn.Refund == RefundStatus.Pending)
        {
            await returns.SendRefundAsync(orderReturn.Id, cancellationToken);
        }

        return TypedResults.Ok(await ResponseAsync(dbContext, orderReturn.Id, cancellationToken));
    }

    // Another try at a refund the provider refused, once whatever stopped it has been put right. Only a refusal can
    // be tried again: a refund nobody heard back about may already have moved the money (D-194).
    private static async Task<Results<Ok<AdminReturnResponse>, NotFound, ProblemHttpResult>> RefundAgainAsync(
        Guid returnId,
        DbContext dbContext,
        OrderReturns returns,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Set<OrderReturn>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == returnId, cancellationToken) is not { } orderReturn)
        {
            return TypedResults.NotFound();
        }

        if (!await returns.ClaimAnotherTryAsync(returnId, cancellationToken))
        {
            return await RefundNotOpenAsync(dbContext, returnId, "Only a refund the provider refused can be tried again", cancellationToken);
        }

        var outcome = await returns.SendRefundAsync(returnId, cancellationToken);

        audit.Record("return.refund-retried", orderReturn.Number, new { outcome = outcome.Status.ToString(), problem = outcome.Problem });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return outcome.Status switch
        {
            RefundStatus.Refunded => TypedResults.Ok(await ResponseAsync(dbContext, returnId, cancellationToken)),
            RefundStatus.Failed => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The refund was refused", detail: outcome.Problem),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The refund was not confirmed", detail: outcome.Problem),
        };
    }

    // The money went back somewhere ShopForge cannot see, and the note says how. The customer is told it is on its
    // way, with the credit note, as they would have been had the provider done it.
    private static async Task<Results<Ok<AdminReturnResponse>, NotFound, ValidationProblem, ProblemHttpResult>> CloseRefundAsync(
        Guid returnId,
        RefundClosingRequest request,
        DbContext dbContext,
        OrderReturns returns,
        IOutbox outbox,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var note = request.Note?.Trim();
        var errors = new RequestErrors()
            .Check(!string.IsNullOrEmpty(note), "note", "Say how the money was given back.")
            .Check(note is null || note.Length <= OrderReturn.MaxRefundTextLength, "note", $"A note is at most {OrderReturn.MaxRefundTextLength} characters.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (await dbContext.Set<OrderReturn>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == returnId, cancellationToken) is not { } orderReturn)
        {
            return TypedResults.NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (!await returns.CloseRefundByHandAsync(returnId, note!, cancellationToken))
        {
            return await RefundNotOpenAsync(dbContext, returnId, "Only a refund that was refused or never confirmed can be closed by hand", cancellationToken);
        }

        var order = await OrderOfAsync(dbContext, orderReturn, cancellationToken);

        outbox.Enqueue(new ReturnRefunded(order.Number, order.Email, orderReturn.Number, orderReturn.RefundedAmount, order.Currency.Code));
        audit.Record("return.refund-closed", orderReturn.Number, new { was = orderReturn.Refund?.ToString(), note });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Ok(await ResponseAsync(dbContext, returnId, cancellationToken));
    }

    private static async Task<ProblemHttpResult> RefundNotOpenAsync(DbContext dbContext, Guid returnId, string title, CancellationToken cancellationToken)
    {
        var refund = await dbContext.Set<OrderReturn>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == returnId)
            .Select(candidate => candidate.Refund)
            .SingleAsync(cancellationToken);

        return TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: title,
            detail: refund is null ? "Nothing has been refunded for this return yet." : $"The refund is {refund}.");
    }

    private static async Task<AdminReturnResponse> ResponseAsync(DbContext dbContext, Guid returnId, CancellationToken cancellationToken) =>
        AdminReturnResponse.From(await dbContext.Set<OrderReturn>().AsNoTracking().SingleAsync(candidate => candidate.Id == returnId, cancellationToken));

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
    List<ReturnLineResponse> Lines,
    // Where the money stands once the parcel is in, and why if it has not gone back (D-194).
    string? Refund,
    string? RefundProblem,
    string? RefundNote,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? RefundSettledAt)
{
    public static AdminReturnResponse From(OrderReturn orderReturn) => new(
        orderReturn.Id,
        orderReturn.Number,
        orderReturn.OrderNumber,
        orderReturn.Status.ToString(),
        orderReturn.RequestedAt,
        orderReturn.RefundedAmount,
        orderReturn.Reason,
        [.. orderReturn.Lines.Select(ReturnLineResponse.From)],
        orderReturn.Refund?.ToString(),
        orderReturn.RefundProblem,
        orderReturn.RefundNote,
        orderReturn.DecidedAt,
        orderReturn.ReceivedAt,
        // Only a refund that is settled has a time it was settled: one tried again keeps the time of the try before,
        // and one the store pays back itself was not settled by anybody ShopForge can see.
        orderReturn.Refund is RefundStatus.Refunded or RefundStatus.ClosedByHand ? orderReturn.RefundSettledAt : null);
}

internal sealed record RefundClosingRequest(string? Note);

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Admin;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Diagnostics;
using ShopForge.Shared.Http;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.Orders.Admin;

// Money a provider took that its order could not take, and the two ways a merchant settles it: given back through
// the provider in one action, or closed by hand once it has been dealt with somewhere else (D-193).
internal static class AdminUnappliedPaymentEndpoints
{
    public static void MapAdminUnappliedPayments(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapGet("/unapplied-payments", GetPaymentsAsync);
        storeAdmin.MapPost("/unapplied-payments/{paymentId:guid}/refund", RefundAsync)
            .RequireAuthorization(AdminPolicies.StoreManagement)
            .Idempotent();
        storeAdmin.MapPost("/unapplied-payments/{paymentId:guid}/close", CloseAsync)
            .RequireAuthorization(AdminPolicies.StoreManagement)
            .Idempotent();
    }

    // Newest first, and only the open ones when that is what is asked for: the list a merchant works through.
    private static async Task<Ok<AdminListResponse<AdminUnappliedPaymentResponse>>> GetPaymentsAsync(
        string? status,
        int? page,
        int? pageSize,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = AdminListQuery.Of(page, pageSize, sort: null, terms: null);
        var wanted = Enum.TryParse<UnappliedPaymentStatus>(status, ignoreCase: true, out var parsed) ? parsed : (UnappliedPaymentStatus?)null;
        var payments = dbContext.Set<UnappliedPayment>()
            .AsNoTracking()
            .Where(payment => wanted == null || payment.Status == wanted);

        var total = await payments.CountAsync(cancellationToken);
        var shown = await payments
            .OrderByDescending(payment => payment.ReceivedAt)
            .ThenByDescending(payment => payment.Id)
            .Skip(query.Skipped)
            .Take(query.Taken)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new AdminListResponse<AdminUnappliedPaymentResponse>(
            [.. shown.Select(AdminUnappliedPaymentResponse.From)], total, query.Page, query.PageSize));
    }

    // The money goes back through the provider that took it. The record is claimed and committed before the call,
    // so a second press finds nothing open and the money is given back once; the outcome is then written down as
    // whatever the provider said, and as not known when it said nothing (D-193).
    private static async Task<Results<Ok<AdminUnappliedPaymentResponse>, NotFound, ProblemHttpResult>> RefundAsync(
        Guid paymentId,
        DbContext dbContext,
        IEnumerable<IPaymentRefunds> paymentRefunds,
        IOutbox outbox,
        IAuditLog audit,
        IShopForgeMetrics metrics,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var payment = await dbContext.Set<UnappliedPayment>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == paymentId, cancellationToken);

        if (payment is null)
        {
            return TypedResults.NotFound();
        }

        var refunds = paymentRefunds.SingleOrDefault(candidate => candidate.Key == payment.Provider);

        if (refunds is null || payment.Reference is not { Length: > 0 } reference)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This payment cannot be refunded from here",
                detail: $"This money cannot be given back through the gateway ({payment.Provider}) from here. Refund it at the gateway, then close it here by hand.");
        }

        if (!await MoveAsync(dbContext, paymentId, UnappliedPaymentStatus.Open, UnappliedPaymentStatus.Refunding, clock, cancellationToken))
        {
            return await AlreadySettledAsync(dbContext, paymentId, cancellationToken);
        }

        var logger = loggerFactory.CreateLogger(typeof(AdminUnappliedPaymentEndpoints));

        try
        {
            await refunds.RefundAsync(new RefundRequest(payment.OrderNumber, reference, payment.Amount, payment.Currency), cancellationToken);
        }
        catch (RefundRefusedException refusal)
        {
            // Refused before any money moved, so the payment is open again and can be tried once the reason is fixed.
            await MoveAsync(dbContext, paymentId, UnappliedPaymentStatus.Refunding, UnappliedPaymentStatus.Open, clock, CancellationToken.None);

            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The refund was refused", detail: refusal.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The provider may have moved the money before the answer was lost — a timeout is this too, though it
            // arrives as a cancellation — so this stays as a refund nobody heard back about. Trying again could give
            // it back twice; looking at the provider cannot.
            logger.LogError(
                exception,
                "Refunding {Provider} payment {Reference} for order {OrderNumber} had no answer; it is left for the store to check.",
                payment.Provider,
                reference,
                payment.OrderNumber);

            return TypedResults.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The refund was not confirmed",
                detail: $"The gateway ({payment.Provider}) did not say whether the money went back. Check the payment there before anything else, then close it here by hand.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Somebody closing it by hand while the provider was being asked is the one way it can have moved on. The
        // money still went back through here, so the audit trail and the customer are told that either way.
        if (!await MoveAsync(dbContext, paymentId, UnappliedPaymentStatus.Refunding, UnappliedPaymentStatus.Refunded, clock, cancellationToken))
        {
            logger.LogWarning(
                "{Provider} payment {Reference} for order {OrderNumber} was refunded after it had been closed by hand.",
                payment.Provider,
                reference,
                payment.OrderNumber);
        }

        var order = await dbContext.Set<Order>().AsNoTracking().SingleAsync(candidate => candidate.Number == payment.OrderNumber, cancellationToken);

        outbox.Enqueue(new UnappliedPaymentRefunded(
            order.Number, order.Email, payment.Amount, payment.Currency.Code, payment.Reason == UnappliedReason.OrderCancelled));
        audit.Record(
            "payment.unapplied-refunded",
            payment.OrderNumber,
            new { payment.Provider, payment.Reference, payment.Amount, currency = payment.Currency.Code });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        metrics.OrderRefunded("unapplied");

        return TypedResults.Ok(await ResponseAsync(dbContext, paymentId, cancellationToken));
    }

    // Dealt with somewhere ShopForge cannot see: given back at the provider's own portal, or settled with the
    // customer another way. The note says which, because the record is all anybody will have later.
    private static async Task<Results<Ok<AdminUnappliedPaymentResponse>, NotFound, ValidationProblem, ProblemHttpResult>> CloseAsync(
        Guid paymentId,
        UnappliedPaymentClosingRequest request,
        DbContext dbContext,
        IAuditLog audit,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var note = request.Note?.Trim();
        var errors = new RequestErrors()
            .Check(!string.IsNullOrEmpty(note), "note", "Say how the payment was dealt with.")
            .Check(note is null || note.Length <= UnappliedPayment.MaxNoteLength, "note", $"A note is at most {UnappliedPayment.MaxNoteLength} characters.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var payment = await dbContext.Set<UnappliedPayment>().SingleOrDefaultAsync(candidate => candidate.Id == paymentId, cancellationToken);

        if (payment is null)
        {
            return TypedResults.NotFound();
        }

        var status = payment.Status;

        if (!payment.CloseByHand(note!, clock.GetUtcNow()))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This payment has already been settled",
                detail: $"It is {status}.");
        }

        audit.Record("payment.unapplied-closed", payment.OrderNumber, new { payment.Provider, payment.Reference, was = status.ToString(), note });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "This payment changed while it was being closed",
                detail: "Load it again and look at what it is now.");
        }

        return TypedResults.Ok(AdminUnappliedPaymentResponse.From(payment));
    }

    // One state to another, and only from the state the caller expects. Whoever loses the race moves nothing.
    private static async Task<bool> MoveAsync(
        DbContext dbContext,
        Guid paymentId,
        UnappliedPaymentStatus from,
        UnappliedPaymentStatus to,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        return await dbContext.Set<UnappliedPayment>()
            .Where(candidate => candidate.Id == paymentId && candidate.Status == from)
            .ExecuteUpdateAsync(
                set => set.SetProperty(candidate => candidate.Status, to).SetProperty(candidate => candidate.ChangedAt, now),
                cancellationToken) == 1;
    }

    private static async Task<ProblemHttpResult> AlreadySettledAsync(DbContext dbContext, Guid paymentId, CancellationToken cancellationToken)
    {
        var status = await dbContext.Set<UnappliedPayment>()
            .AsNoTracking()
            .Where(candidate => candidate.Id == paymentId)
            .Select(candidate => candidate.Status)
            .SingleAsync(cancellationToken);

        return TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "This payment has already been settled",
            detail: $"It is {status}.");
    }

    private static async Task<AdminUnappliedPaymentResponse> ResponseAsync(DbContext dbContext, Guid paymentId, CancellationToken cancellationToken) =>
        AdminUnappliedPaymentResponse.From(
            await dbContext.Set<UnappliedPayment>().AsNoTracking().SingleAsync(candidate => candidate.Id == paymentId, cancellationToken));

    // Everything kept against one order, for the order's own page.
    internal static async Task<List<AdminUnappliedPaymentResponse>> OfOrderAsync(DbContext dbContext, string orderNumber, CancellationToken cancellationToken)
    {
        var payments = await dbContext.Set<UnappliedPayment>()
            .AsNoTracking()
            .Where(payment => payment.OrderNumber == orderNumber)
            .OrderByDescending(payment => payment.ReceivedAt)
            .ToListAsync(cancellationToken);

        return [.. payments.Select(AdminUnappliedPaymentResponse.From)];
    }
}

internal sealed record UnappliedPaymentClosingRequest(string? Note);

// What the admin sees of one. Whether a refund can be asked for is worked out for the screen: an open payment the
// provider named. A provider this deployment cannot refund through is refused when the button is pressed.
internal sealed record AdminUnappliedPaymentResponse(
    Guid Id,
    string OrderNumber,
    string Provider,
    string? Reference,
    decimal Amount,
    string Currency,
    string Reason,
    string Status,
    DateTimeOffset ReceivedAt,
    DateTimeOffset ChangedAt,
    string? Note,
    bool CanRefund)
{
    public static AdminUnappliedPaymentResponse From(UnappliedPayment payment) => new(
        payment.Id,
        payment.OrderNumber,
        payment.Provider,
        payment.Reference,
        payment.Amount,
        payment.Currency.Code,
        payment.Reason.ToString(),
        payment.Status.ToString(),
        payment.ReceivedAt,
        payment.ChangedAt,
        payment.Note,
        payment.Status == UnappliedPaymentStatus.Open && payment.Reference is { Length: > 0 });
}

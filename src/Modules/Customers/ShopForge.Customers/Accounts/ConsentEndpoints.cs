using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Customers.Domain;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Accounts;

// What a shop may write to somebody about, beyond their own orders. Nothing sends on it yet — marketing is Stage
// 34 — but the answer has to be asked for and kept from the day the shop opens, not from the day it first sends
// something (D-119).
internal static class ConsentEndpoints
{
    private const string MarketingStatement = "Send me news and offers from this store by e-mail.";

    public static void MapConsentEndpoints(this IEndpointRouteBuilder account)
    {
        var consents = account.MapGroup("/consents").RequireAuthorization(CustomerPolicies.Customer);

        consents.MapGet("/", GetConsentsAsync);
        consents.MapPut("/marketing", SetMarketingAsync);
    }

    private static async Task<Results<Ok<List<ConsentResponse>>, UnauthorizedHttpResult>> GetConsentsAsync(
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindAsync(cancellationToken) is not { StoreCustomerId: var storeCustomerId })
        {
            return TypedResults.Unauthorized();
        }

        var given = await dbContext.Set<CustomerConsent>()
            .AsNoTracking()
            .Where(consent => consent.StoreCustomerId == storeCustomerId)
            .ToListAsync(cancellationToken);

        // Every purpose is listed, answered or not, so a customer sees what they have never been asked as well as
        // what they said yes to.
        return TypedResults.Ok(Enum.GetValues<ConsentPurpose>()
            .Select(purpose => given.SingleOrDefault(consent => consent.Purpose == purpose) is { } consent
                ? new ConsentResponse(purpose.ToString(), consent.IsGranted, consent.Statement, consent.DecidedAt)
                : new ConsentResponse(purpose.ToString(), false, StatementFor(purpose), null))
            .ToList());
    }

    private static async Task<Results<Ok<ConsentResponse>, UnauthorizedHttpResult>> SetMarketingAsync(
        ConsentRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        ICurrentCustomer currentCustomer,
        IHttpContextAccessor httpContextAccessor,
        IAuditLog audit,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindAsync(cancellationToken) is not { StoreCustomerId: var storeCustomerId })
        {
            return TypedResults.Unauthorized();
        }

        var now = clock.GetUtcNow();
        var address = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var consent = await dbContext.Set<CustomerConsent>()
            .SingleOrDefaultAsync(candidate => candidate.StoreCustomerId == storeCustomerId && candidate.Purpose == ConsentPurpose.Marketing, cancellationToken);

        if (consent is null)
        {
            consent = new CustomerConsent(
                storeContext.StoreId!.Value,
                storeCustomerId,
                ConsentPurpose.Marketing,
                request.IsGranted,
                MarketingStatement,
                address,
                now);
            dbContext.Add(consent);
        }
        else
        {
            consent.Decide(request.IsGranted, MarketingStatement, address, now);
        }

        // Recorded by the customer's id, never their address, so the entry outlives an erasure without undoing it.
        audit.Record("consent.decided", storeCustomerId.ToString(), new { purpose = nameof(ConsentPurpose.Marketing), request.IsGranted });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new ConsentResponse(consent.Purpose.ToString(), consent.IsGranted, consent.Statement, consent.DecidedAt));
    }

    private static string StatementFor(ConsentPurpose purpose) => purpose switch
    {
        ConsentPurpose.Marketing => MarketingStatement,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
    };
}

internal sealed record ConsentRequest(bool IsGranted);

internal sealed record ConsentResponse(string Purpose, bool IsGranted, string Statement, DateTimeOffset? DecidedAt);

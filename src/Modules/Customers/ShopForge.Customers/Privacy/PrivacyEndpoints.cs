using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Customers.Domain;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Privacy;
using ShopForge.Shared.Security;

namespace ShopForge.Customers.Privacy;

// The two things a person can ask of a shop that holds their data: give me all of it, and get rid of it. Both are
// answered for this store only — the same person at another store of the company is a stranger here (D-102).
internal static class PrivacyEndpoints
{
    public static void MapPrivacyEndpoints(this IEndpointRouteBuilder account)
    {
        account.MapGet("/export", ExportAsync).RequireAuthorization(CustomerPolicies.Customer);
        account.MapPost("/delete", DeleteAsync)
            .RequireAuthorization(CustomerPolicies.Customer)
            .RequireRateLimiting(RateLimits.Authentication);
    }

    private static async Task<Results<JsonHttpResult<CustomerExport>, UnauthorizedHttpResult>> ExportAsync(
        ICurrentCustomer currentCustomer,
        IEnumerable<ICustomerData> sources,
        IAuditLog audit,
        DbContext dbContext,
        TimeProvider clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindAsync(cancellationToken) is not { StoreCustomerId: var storeCustomerId })
        {
            return TypedResults.Unauthorized();
        }

        var sections = new Dictionary<string, IReadOnlyList<object>>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            foreach (var section in await source.ExportAsync(storeCustomerId, cancellationToken))
            {
                sections[section.Name] = section.Entries;
            }
        }

        // Asking for a copy is worth recording, but the record must not name them: an entry that outlives an
        // erasure has to say nothing about who it was (D-117).
        audit.Record("customer.exported", storeCustomerId.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);

        httpContext.Response.Headers.ContentDisposition = "attachment; filename=\"shopforge-data.json\"";

        return TypedResults.Json(new CustomerExport(clock.GetUtcNow(), sections));
    }

    private static async Task<Results<NoContent, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(
        DeleteAccountRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        IEnumerable<ICustomerData> sources,
        IPasswordHasher<StoreCustomer> passwordHasher,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindAsync(cancellationToken) is not { StoreCustomerId: var storeCustomerId })
        {
            return TypedResults.Unauthorized();
        }

        var customer = await dbContext.Set<StoreCustomer>().SingleOrDefaultAsync(candidate => candidate.Id == storeCustomerId, cancellationToken);

        if (customer is null)
        {
            return TypedResults.Unauthorized();
        }

        // There is no undoing this, so an open session is not enough on its own.
        if (passwordHasher.VerifyHashedPassword(customer, customer.PasswordHash, request.Password ?? "") == PasswordVerificationResult.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "That is not your password");
        }

        // Erasers save as they go, so one transaction holds all of them: a half-erased customer is worse than
        // either outcome.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (var source in sources)
        {
            await source.EraseAsync(storeCustomerId, cancellationToken);
        }

        audit.Record("customer.erased", storeCustomerId.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await httpContext.SignOutAsync(CustomerPolicies.Scheme);

        return TypedResults.NoContent();
    }
}

internal sealed record DeleteAccountRequest(string? Password);

internal sealed record CustomerExport(DateTimeOffset ExportedAt, IReadOnlyDictionary<string, IReadOnlyList<object>> Sections);

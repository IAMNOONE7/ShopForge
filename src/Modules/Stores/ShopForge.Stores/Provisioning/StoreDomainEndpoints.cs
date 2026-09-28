using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;
using ShopForge.Stores.Resolution;

namespace ShopForge.Stores.Provisioning;

// A store answers on the subdomain the platform gave it and on any name the merchant has proved they own. Adding
// a name is not claiming it: it is served only once the proof is in DNS (D-124).
internal static class StoreDomainEndpoints
{
    public static IEndpointRouteBuilder MapStoreDomainEndpoints(this IEndpointRouteBuilder storeAdmin)
    {
        var domains = storeAdmin.MapGroup("/domains").RequireAuthorization(AdminPolicies.StoreManagement);

        domains.MapGet("/", GetDomainsAsync);
        domains.MapPost("/", AddAsync);
        domains.MapPost("/{domainId:guid}/verify", VerifyAsync);
        domains.MapPost("/{domainId:guid}/primary", MakePrimaryAsync);
        domains.MapDelete("/{domainId:guid}", RemoveAsync);

        return storeAdmin;
    }

    private static async Task<Ok<List<StoreDomainResponse>>> GetDomainsAsync(DbContext dbContext, CancellationToken cancellationToken) =>
        TypedResults.Ok(await dbContext.Set<StoreDomain>()
            .AsNoTracking()
            .OrderByDescending(domain => domain.IsPrimary)
            .ThenBy(domain => domain.HostName)
            .Select(domain => Describe(domain))
            .ToListAsync(cancellationToken));

    private static async Task<Results<Created<StoreDomainResponse>, ValidationProblem, ProblemHttpResult>> AddAsync(
        AddDomainRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var hostName = HostNames.Normalize(request.HostName);
        var errors = new RequestErrors()
            .Check(hostName is not null, "hostName", "A valid host name is required, for example shop.example.com.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        // A name belongs to one store across the whole platform, so the check lifts the filter: the friendly
        // refusal is here and the unique index is what catches two requests arriving together.
        if (await dbContext.Set<StoreDomain>().IgnoreQueryFilters().AnyAsync(domain => domain.HostName == hostName, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "That domain is already taken");
        }

        var store = await CurrentStoreAsync(dbContext, storeContext, cancellationToken);
        var added = store.ClaimDomain(hostName!);

        audit.Record("domain.added", added.HostName);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/stores/{store.Id}/domains/{added.Id}", Describe(added));
    }

    private static async Task<Results<Ok<StoreDomainResponse>, NotFound, ProblemHttpResult>> VerifyAsync(
        Guid domainId,
        DbContext dbContext,
        IDnsTxtRecords dns,
        StoreResolver resolver,
        IAuditLog audit,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var domain = await dbContext.Set<StoreDomain>().SingleOrDefaultAsync(candidate => candidate.Id == domainId, cancellationToken);

        if (domain is null)
        {
            return TypedResults.NotFound();
        }

        if (domain.IsVerified)
        {
            return TypedResults.Ok(Describe(domain));
        }

        var records = await dns.LookupAsync(domain.ChallengeName, cancellationToken);

        if (!records.Contains(domain.VerificationToken, StringComparer.Ordinal))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The record is not there yet",
                detail: $"Add a TXT record at {domain.ChallengeName} with the value shown, then try again. DNS can take a while to spread.");
        }

        domain.Verify(clock.GetUtcNow());
        audit.Record("domain.verified", domain.HostName);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Host lookups are cached, and this host has just started resolving.
        resolver.Forget([domain.HostName]);

        return TypedResults.Ok(Describe(domain));
    }

    private static async Task<Results<Ok<StoreDomainResponse>, NotFound, ProblemHttpResult>> MakePrimaryAsync(
        Guid domainId,
        DbContext dbContext,
        IStoreContext storeContext,
        StoreResolver resolver,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var store = await CurrentStoreAsync(dbContext, storeContext, cancellationToken);
        var domain = store.Domains.SingleOrDefault(candidate => candidate.Id == domainId);

        if (domain is null)
        {
            return TypedResults.NotFound();
        }

        // The primary is where the store calls itself home: links in mail and documents use it, so an unproved
        // name cannot hold the job.
        if (!domain.IsVerified)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "That domain is not proved yet");
        }

        store.MakePrimary(domain);
        audit.Record("domain.made-primary", domain.HostName);
        await dbContext.SaveChangesAsync(cancellationToken);
        resolver.Forget(store.Domains.Select(candidate => candidate.HostName));

        return TypedResults.Ok(Describe(domain));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveAsync(
        Guid domainId,
        DbContext dbContext,
        IStoreContext storeContext,
        StoreResolver resolver,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var store = await CurrentStoreAsync(dbContext, storeContext, cancellationToken);
        var domain = store.Domains.SingleOrDefault(candidate => candidate.Id == domainId);

        if (domain is null)
        {
            return TypedResults.NotFound();
        }

        // Removing the primary would leave the store with no address to call its own.
        if (domain.IsPrimary)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The primary domain cannot be removed",
                detail: "Make another domain primary first.");
        }

        var hostName = domain.HostName;
        store.RemoveDomain(domain);
        dbContext.Remove(domain);
        audit.Record("domain.removed", hostName);
        await dbContext.SaveChangesAsync(cancellationToken);
        resolver.Forget([hostName]);

        return TypedResults.NoContent();
    }

    private static Task<Store> CurrentStoreAsync(DbContext dbContext, IStoreContext storeContext, CancellationToken cancellationToken) =>
        dbContext.Set<Store>()
            .Include(store => store.Domains)
            .IgnoreQueryFilters([TenancyFilters.Store])
            .SingleAsync(store => store.Id == storeContext.StoreId, cancellationToken);

    private static StoreDomainResponse Describe(StoreDomain domain) => new(
        domain.Id,
        domain.HostName,
        domain.IsPrimary,
        domain.IsVerified,
        domain.VerifiedAt,
        domain.IsVerified ? null : domain.ChallengeName,
        domain.IsVerified ? null : domain.VerificationToken);
}

internal sealed record AddDomainRequest(string? HostName);

internal sealed record StoreDomainResponse(
    Guid Id,
    string HostName,
    bool IsPrimary,
    bool IsVerified,
    DateTimeOffset? VerifiedAt,
    string? ChallengeName,
    string? ChallengeValue);

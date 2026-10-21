using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Edge;
using ShopForge.Shared.Http;
using ShopForge.Shared.Provisioning;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;
using ShopForge.Stores.Edge;
using ShopForge.Stores.Resolution;

namespace ShopForge.Stores.Provisioning;

// A store answers on the subdomain the platform gave it and on any name the merchant has proved they own. Adding
// a name is not claiming it: it is served only once the proof is in DNS (D-124). A proved name is then bound at the
// edge, and a removed one taken off it, by operations that report how far they have got (D-212).
internal static class StoreDomainEndpoints
{
    public static IEndpointRouteBuilder MapStoreDomainEndpoints(this IEndpointRouteBuilder storeAdmin)
    {
        var domains = storeAdmin.MapGroup("/domains").RequireAuthorization(AdminPolicies.StoreManagement);

        domains.MapGet("/", GetDomainsAsync);
        domains.MapPost("/", AddAsync);
        domains.MapPost("/{domainId:guid}/verify", VerifyAsync);
        domains.MapPost("/{domainId:guid}/primary", MakePrimaryAsync);
        domains.MapPost("/{domainId:guid}/zone", RunZoneAsync);
        domains.MapDelete("/{domainId:guid}", RemoveAsync);

        return storeAdmin;
    }

    private static async Task<Ok<List<StoreDomainResponse>>> GetDomainsAsync(
        DbContext dbContext, IEdgeDomains edge, PlatformHosts platformHosts, CancellationToken cancellationToken)
    {
        var domains = await dbContext.Set<StoreDomain>()
            .AsNoTracking()
            .OrderByDescending(domain => domain.IsPrimary)
            .ThenBy(domain => domain.HostName)
            .ToListAsync(cancellationToken);
        var place = await EdgePlace.ForStoreAsync(dbContext, edge, platformHosts, cancellationToken);

        return TypedResults.Ok(domains.Select(domain => Describe(domain, place)).ToList());
    }

    private static async Task<Results<Created<StoreDomainResponse>, ValidationProblem, ProblemHttpResult>> AddAsync(
        AddDomainRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        PlatformHosts platformHosts,
        IEdgeDomains edge,
        IAuditLog audit,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var hostName = HostNames.Normalize(request.HostName);
        var errors = new RequestErrors()
            .Check(hostName is not null, "hostName", "A valid host name is required, for example shop.example.com.")
            // A name inside the platform's own zone is not a merchant's to claim: they cannot put the proof in
            // DNS for it, so the claim would sit there holding the name until it lapsed (D-189).
            .Check(
                !platformHosts.Owns(hostName),
                "hostName",
                $"A name under {platformHosts.Suffix} belongs to the platform. Your store already has one; add a domain of your own here.");

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
        var added = store.ClaimDomain(hostName!, clock.GetUtcNow());

        audit.Record("domain.added", added.HostName);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/admin/stores/{store.Id}/domains/{added.Id}",
            Describe(added, await EdgePlace.ForStoreAsync(dbContext, edge, platformHosts, cancellationToken)));
    }

    private static async Task<Results<Ok<StoreDomainResponse>, NotFound, ProblemHttpResult>> VerifyAsync(
        Guid domainId,
        DbContext dbContext,
        IDnsTxtRecords dns,
        StoreResolver resolver,
        DomainBinder binder,
        IEdgeDomains edge,
        PlatformHosts platformHosts,
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
            return TypedResults.Ok(Describe(domain, await EdgePlace.ForStoreAsync(dbContext, edge, platformHosts, cancellationToken)));
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

        // Saved with the proof when it starts: a binding that cannot start yet is the pass's to start (D-212).
        await binder.StartBindingAsync(domain.HostName, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Host lookups are cached, and this host has just started resolving.
        await resolver.ForgetAsync([domain.HostName], cancellationToken);

        return TypedResults.Ok(Describe(domain, await EdgePlace.ForStoreAsync(dbContext, edge, platformHosts, cancellationToken)));
    }

    private static async Task<Results<Ok<StoreDomainResponse>, NotFound, ProblemHttpResult>> MakePrimaryAsync(
        Guid domainId,
        DbContext dbContext,
        IStoreContext storeContext,
        StoreResolver resolver,
        IEdgeDomains edge,
        PlatformHosts platformHosts,
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
        await resolver.ForgetAsync(store.Domains.Select(candidate => candidate.HostName), cancellationToken);

        return TypedResults.Ok(Describe(domain, await EdgePlace.ForStoreAsync(dbContext, edge, platformHosts, cancellationToken)));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> RemoveAsync(
        Guid domainId,
        DbContext dbContext,
        IStoreContext storeContext,
        StoreResolver resolver,
        DomainBinder binder,
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
        await resolver.ForgetAsync([hostName], cancellationToken);

        // The store stops answering on it at once; the edge lets go of it after (D-212).
        await binder.StartUnbindingAsync(hostName, cancellationToken);

        return TypedResults.NoContent();
    }

    // The platform runs DNS for a claim's domain at Cloudflare, so the merchant changes the nameservers at their
    // registrar once and the platform publishes everything else: the proof, the edge's token, the traffic record
    // (D-212). For a domain that has nothing else on it yet, such as one bought for the shop: one with mail or another
    // site keeps its DNS where it is, and its records are published by hand.
    private static async Task<Results<Accepted<OperationStarted>, ValidationProblem, NotFound, ProblemHttpResult>> RunZoneAsync(
        Guid domainId,
        RunZoneRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IDnsZones zones,
        IProvisioning provisioning,
        PlatformHosts platformHosts,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Set<StoreDomain>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == domainId, cancellationToken) is not { } domain)
        {
            return TypedResults.NotFound();
        }

        var zoneName = request.Zone is null ? ApexOf(domain.HostName) : HostNames.Normalize(request.Zone);
        var errors = new RequestErrors()
            .Check(!domain.IsVerified, "domainId", "The domain is proved already, and its DNS stays where it is.")
            .Check(zoneName is not null && zoneName.Contains('.', StringComparison.Ordinal), "zone", "A zone is a domain such as shop.cz.")
            .Check(zoneName is null || domain.HostName == zoneName || domain.HostName.EndsWith($".{zoneName}", StringComparison.Ordinal), "zone",
                "The zone has to be the domain itself or one it is under.")
            .Check(zoneName is null || !platformHosts.Overlaps(zoneName), "zone", "That zone is the platform's own.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (!zones.IsConfigured)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Not here", detail: "This deployment does not run DNS for merchants.");
        }

        // A zone is one store's across the platform, so the check lifts the filter; the unique index catches two at once.
        if (await dbContext.Set<StoreZone>().IgnoreQueryFilters().SingleOrDefaultAsync(zone => zone.Name == zoneName, cancellationToken) is { } existing)
        {
            return existing.StoreId == storeContext.StoreId && existing.OperationId is { } running
                ? TypedResults.Accepted($"operations/{running}", new OperationStarted(running))
                : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "That zone is already taken");
        }

        // Saved with the operation in its one save, then named by it, as a binding is.
        var zone = new StoreZone(storeContext.TenantId!.Value, storeContext.StoreId!.Value, zoneName!, clock.GetUtcNow());
        dbContext.Add(zone);
        var started = await provisioning.StartAsync(EdgeOperations.Zone, EdgeOperations.ZoneSubject(zoneName!), input: null, cancellationToken);

        if (!started.Started)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Not now", detail: "The zone is already being worked on.");
        }

        zone.Begin(started.OperationId, clock.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Accepted($"operations/{started.OperationId}", new OperationStarted(started.OperationId));
    }

    // The last two labels, which is the registrable domain everywhere the first shops sell (.cz, .sk, .eu). A merchant
    // under a longer suffix, such as .co.uk, names the zone.
    private static string ApexOf(string hostName)
    {
        var labels = hostName.Split('.');

        return labels.Length <= 2 ? hostName : string.Join('.', labels[^2..]);
    }

    private static Task<Store> CurrentStoreAsync(DbContext dbContext, IStoreContext storeContext, CancellationToken cancellationToken) =>
        dbContext.Set<Store>()
            .Include(store => store.Domains)
            .IgnoreQueryFilters([TenancyFilters.Store])
            .SingleAsync(store => store.Id == storeContext.StoreId, cancellationToken);

    private static StoreDomainResponse Describe(StoreDomain domain, EdgePlace place) => new(
        domain.Id,
        domain.HostName,
        domain.IsPrimary,
        domain.IsVerified,
        domain.VerifiedAt,
        domain.IsVerified ? null : domain.ChallengeName,
        domain.IsVerified ? null : domain.VerificationToken,
        domain.ClaimExpiresAt,
        place.EdgeOf(domain),
        place.ZoneOf(domain));
}

internal sealed record AddDomainRequest(string? HostName);

internal sealed record RunZoneRequest(string? Zone);

internal sealed record OperationStarted(Guid OperationId);

internal sealed record StoreDomainResponse(
    Guid Id,
    string HostName,
    bool IsPrimary,
    bool IsVerified,
    DateTimeOffset? VerifiedAt,
    string? ChallengeName,
    string? ChallengeValue,
    // When an unproved claim gives the name back. Null once it is proved (D-189).
    DateTimeOffset? ClaimExpiresAt,
    // How far the domain has got at the edge, and the records the merchant publishes when the platform does not run
    // the zone. Null for the platform's own subdomain, and where this deployment binds nothing at the edge.
    EdgeView? Edge,
    // The zone the platform runs for it, and the nameservers the merchant points it at.
    ZoneView? Zone);

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Access;
using ShopForge.Shared.Email;
using ShopForge.Shared.Http;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;
using ShopForge.Stores.Resolution;

namespace ShopForge.Stores.Platform;

// What running the platform amounts to today: see the companies on it, take one on, and close one down. The
// endpoints live here because Stores owns the tenant; who may call them is the platform policy the host applies.
internal static class PlatformTenantEndpoints
{
    public static void MapPlatformTenants(this IEndpointRouteBuilder platform)
    {
        var tenants = platform.MapGroup("/tenants");

        tenants.MapGet("/", GetTenantsAsync);
        tenants.MapPost("/", CreateTenantAsync);
        tenants.MapPost("/{tenantId:guid}/suspend", SuspendAsync);
        tenants.MapPost("/{tenantId:guid}/resume", ResumeAsync);
    }

    private static async Task<Ok<List<PlatformTenantResponse>>> GetTenantsAsync(
        DbContext dbContext,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var tenants = await dbContext.Set<Tenant>().AsNoTracking().OrderBy(tenant => tenant.Name).ToListAsync(cancellationToken);
        var plans = await dbContext.Set<Plan>().AsNoTracking().ToListAsync(cancellationToken);
        var fallback = plans.SingleOrDefault(plan => plan.IsDefault);
        var answer = new List<PlatformTenantResponse>(tenants.Count);

        foreach (var tenant in tenants)
        {
            var plan = plans.SingleOrDefault(candidate => candidate.Id == tenant.PlanId) ?? fallback;

            answer.Add(new PlatformTenantResponse(
                tenant.Id,
                tenant.Name,
                tenant.Status.ToString(),
                plan?.Code,
                await UsageOfAsync(services, tenant.Id, cancellationToken)));
        }

        return TypedResults.Ok(answer);
    }

    // Each module counts its own rows inside the tenant's scope, so the filters stay on and nothing here knows what
    // a product or an order even is (D-105).
    // A company that has not been put on a plan of its own is on the default one (D-108).
    private static Task<string?> PlanCodeOfAsync(DbContext dbContext, Tenant tenant, CancellationToken cancellationToken) =>
        dbContext.Set<Plan>()
            .Where(plan => tenant.PlanId == null ? plan.IsDefault : plan.Id == tenant.PlanId)
            .Select(plan => plan.Code)
            .SingleOrDefaultAsync(cancellationToken);

    private static async Task<List<UsageCount>> UsageOfAsync(IServiceProvider services, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().SetTenant(tenantId);

        // Store-owned rows have no tenant column, so the stores of this company are what scopes them; nothing is
        // counted by lifting a filter and hoping.
        var storeIds = await scope.ServiceProvider.GetRequiredService<DbContext>().Set<Store>()
            .IgnoreQueryFilters([TenancyFilters.Store])
            .Select(store => store.Id)
            .ToListAsync(cancellationToken);

        var counts = new List<UsageCount>();

        foreach (var usage in scope.ServiceProvider.GetServices<ITenantUsage>())
        {
            counts.AddRange(await usage.CountAsync(storeIds, cancellationToken));
        }

        return [.. counts.OrderBy(count => count.Name, StringComparer.Ordinal)];
    }

    private static async Task<Results<Created<PlatformTenantResponse>, ValidationProblem, ProblemHttpResult>> CreateTenantAsync(
        NewTenantRequest request,
        DbContext dbContext,
        IServiceProvider services,
        IEnumerable<ITenantInitializer> initializers,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 200, "name", "Name is required (up to 200 characters).")
            .Check(Emails.IsValid(request.OwnerEmail), "ownerEmail", "A valid e-mail address is required for the owner.")
            .Check(request.OwnerPassword is { Length: >= 10 and <= 128 }, "ownerPassword", "The owner's password needs at least 10 characters.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var tenant = new Tenant(request.Name!);
        dbContext.Add(tenant);
        await dbContext.SaveChangesAsync(cancellationToken);

        // The owner is created inside the new tenant's scope: the save guard would refuse it under any other (D-106).
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().SetTenant(tenant.Id);

        foreach (var initializer in scope.ServiceProvider.GetServices<ITenantInitializer>())
        {
            await initializer.InitializeAsync(tenant.Id, new NewTenantOwner(request.OwnerEmail!, request.OwnerPassword!), cancellationToken);
        }

        return TypedResults.Created(
            $"/api/platform/tenants/{tenant.Id}",
            new PlatformTenantResponse(
                tenant.Id,
                tenant.Name,
                tenant.Status.ToString(),
                await PlanCodeOfAsync(dbContext, tenant, cancellationToken),
                []));
    }

    private static Task<Results<Ok<PlatformTenantResponse>, NotFound, ProblemHttpResult>> SuspendAsync(
        Guid tenantId,
        DbContext dbContext,
        StoreResolver resolver,
        CancellationToken cancellationToken) =>
        ChangeAsync(tenantId, tenant => tenant.Suspend(), "The tenant is already suspended", dbContext, resolver, cancellationToken);

    private static Task<Results<Ok<PlatformTenantResponse>, NotFound, ProblemHttpResult>> ResumeAsync(
        Guid tenantId,
        DbContext dbContext,
        StoreResolver resolver,
        CancellationToken cancellationToken) =>
        ChangeAsync(tenantId, tenant => tenant.Resume(), "The tenant is already active", dbContext, resolver, cancellationToken);

    private static async Task<Results<Ok<PlatformTenantResponse>, NotFound, ProblemHttpResult>> ChangeAsync(
        Guid tenantId,
        Func<Tenant, bool> change,
        string rejection,
        DbContext dbContext,
        StoreResolver resolver,
        CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Set<Tenant>().SingleOrDefaultAsync(candidate => candidate.Id == tenantId, cancellationToken);

        if (tenant is null)
        {
            return TypedResults.NotFound();
        }

        if (!change(tenant))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: rejection);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Host lookups are cached, so a company going dark (or coming back) has to drop its cached entries.
        await resolver.ForgetTenantAsync(tenant.Id, cancellationToken);

        return TypedResults.Ok(new PlatformTenantResponse(
            tenant.Id,
            tenant.Name,
            tenant.Status.ToString(),
            await PlanCodeOfAsync(dbContext, tenant, cancellationToken),
            []));
    }
}

internal sealed record NewTenantRequest(string? Name, string? OwnerEmail, string? OwnerPassword);

internal sealed record PlatformTenantResponse(Guid Id, string Name, string Status, string? PlanCode, List<UsageCount> Usage);

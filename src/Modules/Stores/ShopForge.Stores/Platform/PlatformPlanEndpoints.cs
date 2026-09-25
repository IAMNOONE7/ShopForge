using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Http;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Platform;

// The plans the platform sells and who is on which. Nothing here charges anybody: a plan is a set of caps until
// there is billing to go with it (D-107).
internal static class PlatformPlanEndpoints
{
    public static void MapPlatformPlans(this IEndpointRouteBuilder platform)
    {
        var plans = platform.MapGroup("/plans");

        plans.MapGet("/", GetPlansAsync);
        plans.MapPost("/", CreatePlanAsync);
        plans.MapPut("/{planId:guid}", UpdatePlanAsync);

        platform.MapPut("/tenants/{tenantId:guid}/plan", MoveTenantAsync);
    }

    private static async Task<Ok<List<PlanResponse>>> GetPlansAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var plans = await dbContext.Set<Plan>().AsNoTracking().OrderBy(plan => plan.Name).ToListAsync(cancellationToken);

        return TypedResults.Ok(plans.Select(PlanResponse.From).ToList());
    }

    private static async Task<Results<Created<PlanResponse>, ValidationProblem, ProblemHttpResult>> CreatePlanAsync(
        PlanRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request).Check(!string.IsNullOrWhiteSpace(request.Code), "code", "A code is required.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var plan = new Plan(request.Code!, request.Name!, request.MaxStores, request.MaxProducts);

        if (await dbContext.Set<Plan>().AnyAsync(existing => existing.Code == plan.Code, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "A plan with this code already exists");
        }

        dbContext.Add(plan);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/platform/plans/{plan.Id}", PlanResponse.From(plan));
    }

    // Changing what a plan covers applies to everybody on it at once, and only stops what they do next (D-108).
    private static async Task<Results<Ok<PlanResponse>, ValidationProblem, NotFound>> UpdatePlanAsync(
        Guid planId,
        PlanRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var plan = await dbContext.Set<Plan>().SingleOrDefaultAsync(candidate => candidate.Id == planId, cancellationToken);

        if (plan is null)
        {
            return TypedResults.NotFound();
        }

        plan.Update(request.Name!, request.MaxStores, request.MaxProducts);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(PlanResponse.From(plan));
    }

    private static async Task<Results<Ok<PlanResponse>, NotFound>> MoveTenantAsync(
        Guid tenantId,
        MoveTenantRequest request,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var tenant = await dbContext.Set<Tenant>().SingleOrDefaultAsync(candidate => candidate.Id == tenantId, cancellationToken);
        var plan = await dbContext.Set<Plan>().SingleOrDefaultAsync(candidate => candidate.Id == request.PlanId, cancellationToken);

        if (tenant is null || plan is null)
        {
            return TypedResults.NotFound();
        }

        tenant.MoveTo(plan);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(PlanResponse.From(plan));
    }

    private static RequestErrors Validate(PlanRequest request) =>
        new RequestErrors()
            .Check(!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 100, "name", "A name is required (up to 100 characters).")
            .Check(request.MaxStores is null or >= 0, "maxStores", "A cap is zero or more, or nothing at all for no cap.")
            .Check(request.MaxProducts is null or >= 0, "maxProducts", "A cap is zero or more, or nothing at all for no cap.");
}

internal sealed record PlanRequest(string? Code, string? Name, int? MaxStores, int? MaxProducts);

internal sealed record MoveTenantRequest(Guid PlanId);

internal sealed record PlanResponse(Guid Id, string Code, string Name, int? MaxStores, int? MaxProducts, bool IsDefault)
{
    public static PlanResponse From(Plan plan) => new(plan.Id, plan.Code, plan.Name, plan.MaxStores, plan.MaxProducts, plan.IsDefault);
}

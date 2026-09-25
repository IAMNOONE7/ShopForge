using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Platform;
using ShopForge.Stores.Platform;

namespace ShopForge.Stores.Admin;

// What the company is on and what it is using, for the people who run it: the same numbers the platform sees, for
// their own tenant only.
internal static class AdminPlanEndpoint
{
    public static RouteHandlerBuilder MapAdminPlan(this IEndpointRouteBuilder tenantAdmin) =>
        tenantAdmin.MapGet("/plan", GetPlanAsync);

    private static async Task<Ok<AdminPlanResponse>> GetPlanAsync(
        TenantPlans plans,
        IEnumerable<ITenantUsage> usages,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var plan = await plans.ForCurrentTenantAsync(cancellationToken);
        var storeIds = await plans.StoreIdsAsync(cancellationToken);
        var counts = new List<UsageCount>();

        foreach (var usage in usages)
        {
            counts.AddRange(await usage.CountAsync(storeIds, cancellationToken));
        }

        return TypedResults.Ok(new AdminPlanResponse(
            plan?.Code,
            plan?.Name,
            plan?.MaxStores,
            plan?.MaxProducts,
            [.. counts.OrderBy(count => count.Name, StringComparer.Ordinal)]));
    }
}

internal sealed record AdminPlanResponse(string? Code, string? Name, int? MaxStores, int? MaxProducts, List<UsageCount> Usage);

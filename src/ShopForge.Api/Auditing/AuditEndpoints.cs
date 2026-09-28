using ShopForge.Infrastructure.Auditing;
using ShopForge.Shared.Security;

namespace ShopForge.Api.Auditing;

// Reading the record: a company sees what was done to it, the platform sees what it did to a company and what it
// did on its own. Nothing here writes, and there is no endpoint anywhere that changes an entry (D-116).
internal static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuditEndpoints(this IEndpointRouteBuilder tenantAdmin)
    {
        tenantAdmin.MapGet("/audit", async (
                [AsParameters] AuditQueryParameters parameters,
                AuditReader reader,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await reader.ForCurrentTenantAsync(parameters.ToQuery(), cancellationToken)))
            .RequireAuthorization(AdminPolicies.StoreManagement);

        return tenantAdmin;
    }

    public static IEndpointRouteBuilder MapPlatformAuditEndpoints(this IEndpointRouteBuilder platformOperator)
    {
        platformOperator.MapGet("/tenants/{tenantId:guid}/audit", async (
            Guid tenantId,
            [AsParameters] AuditQueryParameters parameters,
            AuditReader reader,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await reader.ForTenantAsync(tenantId, parameters.ToQuery(), cancellationToken)));

        platformOperator.MapGet("/audit", async (
            [AsParameters] AuditQueryParameters parameters,
            AuditReader reader,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await reader.ForThePlatformAsync(parameters.ToQuery(), cancellationToken)));

        return platformOperator;
    }
}

internal readonly record struct AuditQueryParameters(Guid? StoreId, string? Action, DateTimeOffset? From, DateTimeOffset? To, int? Page, int? PageSize)
{
    public AuditQuery ToQuery() => new(StoreId, Action, From, To, Page ?? 1, PageSize ?? 50);
}

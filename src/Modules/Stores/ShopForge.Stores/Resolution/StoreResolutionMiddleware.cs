using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Resolution;

internal sealed class StoreResolutionMiddleware(RequestDelegate next, ILogger<StoreResolutionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, StoreResolver resolver, StoreContext storeContext)
    {
        var metadata = context.GetEndpoint()?.Metadata;
        ResolvedStore? store;

        if (metadata?.GetMetadata<StoreRequiredMetadata>() is not null)
        {
            store = await resolver.ResolveAsync(context.Request.Host.Host, context.RequestAborted);
        }
        else if (metadata?.GetMetadata<AdminScopeMetadata>() is { } adminScope)
        {
            if (!Guid.TryParse(context.User.FindFirstValue(ShopForgeClaimTypes.TenantId), out var tenantId))
            {
                await TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
                return;
            }

            storeContext.SetTenant(tenantId);

            if (!adminScope.RequiresStore)
            {
                await next(context);
                return;
            }

            store = Guid.TryParse(context.Request.RouteValues["storeId"] as string, out var storeId)
                ? await resolver.FindForTenantAsync(storeId, tenantId, context.RequestAborted)
                : null;
        }
        else
        {
            await next(context);
            return;
        }

        if (store is null)
        {
            await TypedResults.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Store not found",
                    detail: "The requested store does not exist or is not accessible.")
                .ExecuteAsync(context);
            return;
        }

        storeContext.Set(store.StoreId, store.TenantId);

        using (logger.BeginScope(new Dictionary<string, object> { ["StoreId"] = store.StoreId }))
        {
            await next(context);
        }
    }
}

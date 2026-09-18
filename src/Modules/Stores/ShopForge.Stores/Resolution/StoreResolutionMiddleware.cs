using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Resolution;

internal sealed class StoreResolutionMiddleware(RequestDelegate next, ILogger<StoreResolutionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, StoreResolver resolver, StoreContext storeContext)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<StoreRequiredMetadata>() is null)
        {
            await next(context);
            return;
        }

        var store = await resolver.ResolveAsync(context.Request.Host.Host, context.RequestAborted);

        if (store is null)
        {
            await TypedResults.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Store not found",
                    detail: "No store is configured for the requested host.")
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

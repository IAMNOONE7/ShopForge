using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Stores.Resolution;
using ShopForge.Stores.Storefront;

namespace ShopForge.Stores;

public static class StoresModule
{
    internal const string Schema = "stores";

    public static Assembly Assembly => typeof(StoresModule).Assembly;

    public static IServiceCollection AddStoresModule(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<StoreResolver>();

        return services;
    }

    public static IApplicationBuilder UseStoreResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<StoreResolutionMiddleware>();

    public static TBuilder RequireStore<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new StoreRequiredMetadata());

    public static IEndpointRouteBuilder MapStoresStorefrontEndpoints(this IEndpointRouteBuilder storefront)
    {
        storefront.MapStorefrontStore();

        return storefront;
    }
}

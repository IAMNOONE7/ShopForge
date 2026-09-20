using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Stores;
using ShopForge.Stores.Admin;
using ShopForge.Stores.Logos;
using ShopForge.Stores.Provisioning;
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
        services.AddScoped<IStorePublishCheck, StoreBrandingPublishCheck>();

        return services;
    }

    public static IApplicationBuilder UseStoreResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<StoreResolutionMiddleware>();

    public static TBuilder RequireStore<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new StoreRequiredMetadata());

    public static TBuilder RequireAdminTenant<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AdminScopeMetadata(RequiresStore: false));

    public static TBuilder RequireAdminStore<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AdminScopeMetadata(RequiresStore: true));

    public static IEndpointRouteBuilder MapStoresAdminEndpoints(this IEndpointRouteBuilder tenantAdmin)
    {
        tenantAdmin.MapAdminStores();
        tenantAdmin.MapStoreProvisioning();

        return tenantAdmin;
    }

    public static IEndpointRouteBuilder MapStoresStoreAdminEndpoints(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapAdminStoreLogo();
        storeAdmin.MapStoreLifecycle();

        return storeAdmin;
    }

    public static IEndpointRouteBuilder MapStoresStorefrontEndpoints(this IEndpointRouteBuilder storefront)
    {
        storefront.MapStorefrontStore();
        storefront.MapStorefrontLogo();

        return storefront;
    }
}

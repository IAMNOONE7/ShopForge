using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Catalog.Admin;
using ShopForge.Catalog.Import;
using ShopForge.Catalog.Privacy;
using ShopForge.Catalog.Publishing;
using ShopForge.Catalog.Reviews;
using ShopForge.Catalog.Storefront;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Privacy;
using ShopForge.Shared.Stores;

namespace ShopForge.Catalog;

public static class CatalogModule
{
    internal const string Schema = "catalog";

    public static Assembly Assembly => typeof(CatalogModule).Assembly;

    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<IStorePublishCheck, CatalogPublishCheck>();
        services.AddScoped<ISellableProducts, SellableProductLookup>();
        services.AddScoped<ITenantProducts, TenantProductLookup>();
        services.AddScoped<ITenantUsage, ProductUsage>();
        services.AddScoped<ProductRatings>();
        services.AddScoped<ICustomerData, CustomerReviewData>();

        return services;
    }

    public static IEndpointRouteBuilder MapCatalogTenantAdminEndpoints(this IEndpointRouteBuilder tenantAdmin)
    {
        tenantAdmin.MapAdminProducts();

        return tenantAdmin;
    }

    public static IEndpointRouteBuilder MapCatalogStoreAdminEndpoints(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapAdminStoreCatalog();
        storeAdmin.MapAdminReviews();
        storeAdmin.MapAdminAttributes();
        storeAdmin.MapCatalogImport();

        return storeAdmin;
    }

    public static IEndpointRouteBuilder MapCatalogStorefrontEndpoints(this IEndpointRouteBuilder storefront)
    {
        storefront.MapStorefrontCatalog();
        storefront.MapStorefrontReviews();

        return storefront;
    }
}

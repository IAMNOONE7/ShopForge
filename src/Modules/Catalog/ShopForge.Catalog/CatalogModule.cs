using System.Reflection;
using Microsoft.AspNetCore.Routing;
using ShopForge.Catalog.Admin;
using ShopForge.Catalog.Storefront;

namespace ShopForge.Catalog;

public static class CatalogModule
{
    internal const string Schema = "catalog";

    public static Assembly Assembly => typeof(CatalogModule).Assembly;

    public static IEndpointRouteBuilder MapCatalogTenantAdminEndpoints(this IEndpointRouteBuilder tenantAdmin)
    {
        tenantAdmin.MapAdminProducts();

        return tenantAdmin;
    }

    public static IEndpointRouteBuilder MapCatalogStoreAdminEndpoints(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapAdminStoreCatalog();
        storeAdmin.MapAdminAttributes();

        return storeAdmin;
    }

    public static IEndpointRouteBuilder MapCatalogStorefrontEndpoints(this IEndpointRouteBuilder storefront)
    {
        storefront.MapStorefrontCatalog();

        return storefront;
    }
}

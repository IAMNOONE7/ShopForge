using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Inventory.Admin;
using ShopForge.Inventory.Stock;
using ShopForge.Shared.Inventory;

namespace ShopForge.Inventory;

public static class InventoryModule
{
    internal const string Schema = "inventory";

    public static Assembly Assembly => typeof(InventoryModule).Assembly;

    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddScoped<IStockLedger, StockLedger>();

        return services;
    }

    public static IEndpointRouteBuilder MapInventoryTenantAdminEndpoints(this IEndpointRouteBuilder tenantAdmin)
    {
        tenantAdmin.MapAdminStock();

        return tenantAdmin;
    }
}

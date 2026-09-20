using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Orders.Admin;
using ShopForge.Orders.Background;
using ShopForge.Orders.Payments;
using ShopForge.Orders.Publishing;
using ShopForge.Orders.Storefront;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Stores;

namespace ShopForge.Orders;

public static class OrdersModule
{
    internal const string Schema = "orders";

    public static Assembly Assembly => typeof(OrdersModule).Assembly;

    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        services.AddScoped<IPaymentProvider, ManualPaymentProvider>();
        services.AddScoped<IStorePublishCheck, OrdersPublishCheck>();
        services.AddScoped<IStoreInitializer, DefaultStoreMethods>();
        services.AddSingleton<ExpiredOrders>();
        services.AddHostedService<ExpiredOrderSweeper>();

        return services;
    }

    public static IEndpointRouteBuilder MapOrdersStorefrontEndpoints(this IEndpointRouteBuilder storefront)
    {
        storefront.MapCart();
        storefront.MapCheckout();

        return storefront;
    }

    public static IEndpointRouteBuilder MapOrdersStoreAdminEndpoints(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapAdminOrders();
        storeAdmin.MapAdminMethods();

        return storeAdmin;
    }
}

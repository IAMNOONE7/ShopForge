using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Orders.Admin;
using ShopForge.Orders.Background;
using ShopForge.Orders.Catalog;
using ShopForge.Orders.Discounts;
using ShopForge.Orders.Invoicing;
using ShopForge.Orders.Notifications;
using ShopForge.Orders.Payments;
using ShopForge.Orders.Privacy;
using ShopForge.Orders.Publishing;
using ShopForge.Orders.Returns;
using ShopForge.Orders.Shipping;
using ShopForge.Orders.Storefront;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Email;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Privacy;
using ShopForge.Shared.Shipping;
using ShopForge.Shared.Stores;

namespace ShopForge.Orders;

public static class OrdersModule
{
    internal const string Schema = "orders";

    public static Assembly Assembly => typeof(OrdersModule).Assembly;

    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        services.AddScoped<IPaymentProvider, ManualPaymentProvider>();
        services.AddScoped<IPaymentAttempts, PaymentAttempts>();
        services.AddScoped<IShippingProvider, StoreShippingProvider>();
        services.AddScoped<IStoreShippingRates, StoreShippingRates>();
        services.AddScoped<IStorePublishCheck, OrdersPublishCheck>();
        services.AddScoped<IStoreInitializer, DefaultStoreMethods>();
        services.AddScoped<ICustomerOrders, GuestOrderClaim>();
        services.AddScoped<ICustomerPurchases, CustomerPurchases>();
        services.AddScoped<ITenantUsage, OrderUsage>();
        services.AddScoped<ICustomerData, CustomerOrderData>();
        services.AddScoped<IEmailAttachments, OrderAttachments>();
        services.AddScoped<IEventHandler<OrderPlaced>, OrderNotifications>();
        services.AddScoped<IEventHandler<PaymentReceived>, OrderNotifications>();
        services.AddScoped<IEventHandler<PaymentReceived>, InvoiceIssuing>();
        services.AddScoped<Invoices>();
        services.AddScoped<DiscountCodes>();
        services.AddScoped<OrderReturns>();
        services.AddScoped<IEventHandler<OrderCancelled>, OrderNotifications>();
        services.AddScoped<IEventHandler<ShipmentCreated>, OrderNotifications>();
        services.AddScoped<IEventHandler<ReturnDecided>, OrderNotifications>();
        services.AddScoped<IEventHandler<ReturnRefunded>, OrderNotifications>();
        services.AddScoped<IStoreMaintenance, CartCleanup>();
        services.AddScoped<ISoldListings, SoldListings>();
        services.AddScoped<PaymentResults>();
        services.AddScoped<IStoreCatchUp, PaymentReconciliation>();
        services.AddSingleton<ExpiredOrders>();
        services.AddHostedService<ExpiredOrderSweeper>();

        return services;
    }

    public static IEndpointRouteBuilder MapOrdersStorefrontEndpoints(this IEndpointRouteBuilder storefront)
    {
        storefront.MapCart();
        storefront.MapCheckout();
        storefront.MapCustomerOrders();
        storefront.MapCustomerReturns();
        storefront.MapStorefrontDocuments();

        return storefront;
    }

    public static IEndpointRouteBuilder MapPaymentWebhookEndpoints(this IEndpointRouteBuilder payments)
    {
        payments.MapPaymentWebhooks();

        return payments;
    }

    public static IEndpointRouteBuilder MapOrdersStoreAdminEndpoints(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapAdminOrders();
        storeAdmin.MapAdminMethods();
        storeAdmin.MapAdminPickupPoints();
        storeAdmin.MapAdminDiscounts();
        storeAdmin.MapAdminReturns();
        storeAdmin.MapAdminDocuments();

        return storeAdmin;
    }
}

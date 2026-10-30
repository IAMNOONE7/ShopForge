using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Catalog.Admin;
using ShopForge.Catalog.Bulk;
using ShopForge.Catalog.Feeds;
using ShopForge.Catalog.Feeds.Google;
using ShopForge.Catalog.Feeds.Heureka;
using ShopForge.Catalog.Feeds.Zbozi;
using ShopForge.Catalog.Import;
using ShopForge.Catalog.Privacy;
using ShopForge.Catalog.Publishing;
using ShopForge.Catalog.Retiring;
using ShopForge.Catalog.Reviews;
using ShopForge.Catalog.Search;
using ShopForge.Catalog.Seo;
using ShopForge.Catalog.Storefront;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Feeds;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Messaging;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Privacy;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tax;

namespace ShopForge.Catalog;

public static class CatalogModule
{
    internal const string Schema = "catalog";

    public static Assembly Assembly => typeof(CatalogModule).Assembly;

    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<IStorePublishCheck, CatalogPublishCheck>();
        services.AddScoped<ISellableProducts, SellableProductLookup>();
        services.AddScoped<ITaxClassUsers, ListingsNamingATaxClass>();
        services.AddScoped<ITenantProducts, TenantProductLookup>();
        services.AddScoped<ITenantUsage, ProductUsage>();
        services.AddScoped<ProductRatings>();
        services.AddScoped<ICustomerData, CustomerReviewData>();
        services.AddScoped<IProductFeeds, ProductFeeds>();
        services.AddSingleton<IProductFeedFormat, GoogleMerchantFeed>();
        services.AddSingleton<IProductFeedFormat, HeurekaFeed>();
        services.AddSingleton<IProductFeedFormat, ZboziFeed>();
        services.AddScoped<IStoreMaintenance, FeedRefresh>();
        services.AddScoped<WhatPointsAtIt>();
        services.AddScoped<SearchIndex>();
        services.AddScoped<IStoreMaintenance, SearchIndexRebuild>();
        services.AddScoped<IEventHandler<StoreCultureChanged>, SearchCultureChange>();
        services.AddScoped<IStoreMaintenance, SearchLogCleanup>();
        services.AddScoped<ISearchLog, SearchLog>();

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
        storeAdmin.MapAdminBulkListings();
        storeAdmin.MapAdminReviews();
        storeAdmin.MapAdminAttributes();
        storeAdmin.MapCatalogImport();
        storeAdmin.MapAdminFeeds();

        return storeAdmin;
    }

    public static IEndpointRouteBuilder MapCatalogStorefrontEndpoints(this IEndpointRouteBuilder storefront)
    {
        storefront.MapStorefrontCatalog();
        storefront.MapStorefrontReviews();

        // Their own window: a crawler's rhythm is not a shopper's (D-167).
        storefront.MapGroup(string.Empty).RequireRateLimiting(RateLimits.Crawlers).MapSeoDocuments();
        storefront.MapProductFeeds();

        return storefront;
    }

    // At the root of every shop's host, where a crawler looks for them. A sitemap there may list every page on the host,
    // which one under /api/storefront may not (D-206). The ones under /api/storefront stay for whoever already uses them.
    public static IEndpointRouteBuilder MapCatalogSiteRootEndpoints(this IEndpointRouteBuilder site)
    {
        site.MapGroup(string.Empty).RequireRateLimiting(RateLimits.Crawlers).MapSeoDocuments();

        return site;
    }
}

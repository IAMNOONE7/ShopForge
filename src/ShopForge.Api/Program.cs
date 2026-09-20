using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpLogging;
using ShopForge.Access;
using ShopForge.Access.Development;
using ShopForge.Api.Errors;
using ShopForge.Api.Health;
using ShopForge.Catalog;
using ShopForge.Catalog.Development;
using ShopForge.Infrastructure;
using ShopForge.Infrastructure.Files;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Orders;
using ShopForge.Orders.Development;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores;
using ShopForge.Stores.Development;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddExceptionHandler<UniqueViolationExceptionHandler>();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

builder.Services.AddScoped<StoreContext>();
builder.Services.AddScoped<IStoreContext>(provider => provider.GetRequiredService<StoreContext>());

builder.Services.AddInfrastructure(builder.Configuration, [StoresModule.Assembly, AccessModule.Assembly, CatalogModule.Assembly, OrdersModule.Assembly]);
builder.Services.AddStoresModule();
builder.Services.AddAccessModule();
builder.Services.AddCatalogModule();
builder.Services.AddOrdersModule();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ShopForgeDbContext>("database", tags: [HealthEndpoints.ReadinessTag]);

var app = builder.Build();

app.UseHttpLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseStoreResolution();

app.MapHealthEndpoints();

var storefront = app.MapGroup("/api/storefront").RequireStore();
storefront.MapStoresStorefrontEndpoints();
storefront.MapCatalogStorefrontEndpoints();
storefront.MapOrdersStorefrontEndpoints();

var admin = app.MapGroup("/api/admin");
admin.MapAccessAdminEndpoints();

var tenantAdmin = app.MapGroup("/api/admin")
    .RequireAuthorization(AdminPolicies.TenantUser)
    .RequireAdminTenant();
tenantAdmin.MapStoresAdminEndpoints();
tenantAdmin.MapCatalogTenantAdminEndpoints();

var storeAdmin = tenantAdmin.MapGroup("/stores/{storeId:guid}").RequireAdminStore();
storeAdmin.MapStoresStoreAdminEndpoints();
storeAdmin.MapCatalogStoreAdminEndpoints();
storeAdmin.MapOrdersStoreAdminEndpoints();

if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateDatabaseAsync();
    await app.Services.CreateFileStorageContainerAsync();
    var demo = await app.Services.SeedDevelopmentStoresAsync();
    await app.Services.SeedDevelopmentUsersAsync(demo.TenantId);
    await app.Services.SeedDevelopmentCatalogAsync(demo.TenantId, demo.WoodenHomeStoreId, demo.VoltElectronicsStoreId);
    await app.Services.SeedDevelopmentMethodsAsync(demo.TenantId, [demo.WoodenHomeStoreId, demo.VoltElectronicsStoreId]);
}

await app.RunAsync();

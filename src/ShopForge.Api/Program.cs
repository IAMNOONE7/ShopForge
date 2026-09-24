using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.RateLimiting;
using ShopForge.Access;
using ShopForge.Access.Development;
using ShopForge.Api.Diagnostics;
using ShopForge.Api.Errors;
using ShopForge.Api.Health;
using ShopForge.Api.Messaging;
using ShopForge.Catalog;
using ShopForge.Catalog.Development;
using ShopForge.Customers;
using ShopForge.Infrastructure;
using ShopForge.Infrastructure.Files;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Inventory;
using ShopForge.Orders;
using ShopForge.Orders.Development;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores;
using ShopForge.Stores.Development;

var builder = WebApplication.CreateBuilder(args);

// Connection strings, provider keys and the telemetry connection live in Key Vault and are read with the container
// app's own identity (D-076); locally there is no vault and Compose credentials apply.
if (builder.Configuration["KeyVault:Uri"] is { Length: > 0 } keyVaultUri)
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new Azure.Identity.DefaultAzureCredential());
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddEdgeHeaders();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddExceptionHandler<MalformedRequestExceptionHandler>();
builder.Services.AddExceptionHandler<UniqueViolationExceptionHandler>();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

// Sign-in and password endpoints are the cheapest thing to brute-force, so they get a window of their own.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimits.Authentication, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:Authentication:PermitLimit", 10),
            Window = TimeSpan.FromMinutes(1),
        }));
});

builder.Services.AddScoped<StoreContext>();
builder.Services.AddScoped<IStoreContext>(provider => provider.GetRequiredService<StoreContext>());

builder.Services.AddInfrastructure(
    builder.Configuration,
    [
        StoresModule.Assembly,
        AccessModule.Assembly,
        CatalogModule.Assembly,
        CustomersModule.Assembly,
        InventoryModule.Assembly,
        OrdersModule.Assembly,
    ]);
builder.Services.AddStoresModule();
// Outside Development the browser always reaches the platform over TLS terminated at the edge (D-075). The setting
// exists so a test host can serve plain HTTP; a deployment leaves it alone.
var requireSecureCookies = builder.Configuration.GetValue("Security:RequireSecureCookies", !builder.Environment.IsDevelopment());
builder.Services.AddAccessModule(requireSecureCookies);
builder.Services.AddCatalogModule();
builder.Services.AddCustomersModule(requireSecureCookies);
builder.Services.AddInventoryModule();
builder.Services.AddOrdersModule();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ShopForgeDbContext>("database", tags: [HealthEndpoints.ReadinessTag])
    .AddCheck<FileStorageHealthCheck>("file-storage", tags: [HealthEndpoints.ReadinessTag]);

var app = builder.Build();

app.UseEdgeHeaders();
app.UseMiddleware<CorrelationMiddleware>();
app.UseHttpLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseStoreResolution();

app.MapHealthEndpoints();

var storefront = app.MapGroup("/api/storefront").RequireStore();
storefront.MapStoresStorefrontEndpoints();
storefront.MapCatalogStorefrontEndpoints();
storefront.MapCustomersStorefrontEndpoints();
storefront.MapOrdersStorefrontEndpoints();

app.MapGroup("/api/payments").MapPaymentWebhookEndpoints();

var admin = app.MapGroup("/api/admin");
admin.MapAccessAdminEndpoints();

var tenantAdmin = app.MapGroup("/api/admin")
    .RequireAuthorization(AdminPolicies.TenantUser)
    .RequireAdminTenant();
tenantAdmin.MapStoresAdminEndpoints();
tenantAdmin.MapCatalogTenantAdminEndpoints();
tenantAdmin.MapInventoryTenantAdminEndpoints();

var storeAdmin = tenantAdmin.MapGroup("/stores/{storeId:guid}").RequireAdminStore();
storeAdmin.MapStoresStoreAdminEndpoints();
storeAdmin.MapCatalogStoreAdminEndpoints();
storeAdmin.MapOrdersStoreAdminEndpoints();
storeAdmin.MapAdminOutboxEndpoints();

// Outside Development the schema is migrated by the pipeline before a new revision starts (D-074).
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

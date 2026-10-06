using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.RateLimiting;
using ShopForge.Access;
using ShopForge.Access.Development;
using ShopForge.Api.Auditing;
using ShopForge.Api.Connections;
using ShopForge.Api.Diagnostics;
using ShopForge.Api.Email;
using ShopForge.Api.Errors;
using ShopForge.Api.Health;
using ShopForge.Api.Messaging;
using ShopForge.Api.Security;
using ShopForge.Catalog;
using ShopForge.Catalog.Development;
using ShopForge.Customers;
using ShopForge.Infrastructure;
using ShopForge.Infrastructure.Files;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Inventory;
using ShopForge.Orders;
using ShopForge.Orders.Development;
using ShopForge.Platform;
using ShopForge.Platform.Development;
using ShopForge.Shared.Http;
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
builder.Services.AddExceptionHandler<ConcurrentChangeExceptionHandler>();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

// Three named windows and a ceiling under all of them. The ceiling is the important one: an endpoint nobody
// remembered to limit is still limited, which is the failure this stage exists to prevent (D-126).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimits.Authentication, context =>
        Window(context, "auth", builder.Configuration.GetValue("RateLimiting:Authentication:PermitLimit", 10)));
    options.AddPolicy(RateLimits.Writes, context =>
        Window(context, "writes", builder.Configuration.GetValue("RateLimiting:Writes:PermitLimit", 120)));
    options.AddPolicy(RateLimits.Expensive, context =>
        Window(context, "expensive", builder.Configuration.GetValue("RateLimiting:Expensive:PermitLimit", 10)));
    options.AddPolicy(RateLimits.Crawlers, context =>
        Window(context, "crawlers", builder.Configuration.GetValue("RateLimiting:Crawlers:PermitLimit", 300)));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/health")
            ? RateLimitPartition.GetNoLimiter("health")
            : Window(context, "all", builder.Configuration.GetValue("RateLimiting:Global:PermitLimit", 600)));

    static RateLimitPartition<string> Window(HttpContext context, string bucket, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{bucket}:{Caller(context)}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1) });

    // Per account where there is one, per address otherwise. An address is a poor identity — a household or an
    // office shares one — and somebody signed in is the thing actually worth counting (D-126).
    static string Caller(HttpContext context) =>
        context.User.FindFirstValue(ShopForgeClaimTypes.StoreCustomerId)
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
});

// A body larger than this is refused by the server before a handler sees it; the two endpoints that take a file
// raise it to what they actually accept.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = 1024 * 1024;
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
        PlatformModule.Assembly,
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
builder.Services.AddPlatformModule(requireSecureCookies);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ShopForgeDbContext>("database", tags: [HealthEndpoints.ReadinessTag])
    .AddCheck<FileStorageHealthCheck>("file-storage", tags: [HealthEndpoints.ReadinessTag])
    .AddCheck<SecretStoreHealthCheck>("secret-store", tags: [HealthEndpoints.ReadinessTag]);

var app = builder.Build();

app.UseEdgeHeaders();
app.UseSecurityHeaders();

// A request that carries an idempotency key has its body read twice: once to bind it, once to fingerprint it, so
// the same key used for a different request is caught rather than answered with somebody else's order (D-131).
// Nothing else pays for the buffering, and the body is capped at a megabyte either way (D-126).
app.Use(async (context, next) =>
{
    if (context.Request.Headers.ContainsKey(Idempotency.HeaderName))
    {
        context.Request.EnableBuffering();
    }

    await next(context);
});
app.UseMiddleware<CorrelationMiddleware>();
app.UseHttpLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseStoreResolution();

app.MapHealthEndpoints();

var storefront = app.MapGroup("/api/storefront").RequireStore().RequireRateLimiting(RateLimits.Writes);
storefront.MapStoresStorefrontEndpoints();
storefront.MapCatalogStorefrontEndpoints();
storefront.MapCustomersStorefrontEndpoints();
storefront.MapOrdersStorefrontEndpoints();

app.MapGroup("/api/payments").MapPaymentWebhookEndpoints();
app.MapEmailWebhookEndpoints();

// Administering ShopForge itself: outside tenancy, behind its own cookie (D-103).
var platform = app.MapGroup("/api/platform").RequireRateLimiting(RateLimits.Writes);
platform.MapPlatformAuthEndpoints();
var platformOperator = platform.MapGroup(string.Empty).RequireAuthorization(PlatformPolicies.PlatformUser);
platformOperator.MapStoresPlatformEndpoints();
platformOperator.MapPlatformOperatorEndpoints();
platformOperator.MapPlatformOutboxEndpoints();
platformOperator.MapPlatformAuditEndpoints();

var admin = app.MapGroup("/api/admin").RequireRateLimiting(RateLimits.Writes);
admin.MapAccessAdminEndpoints();

var tenantAdmin = app.MapGroup("/api/admin")
    .RequireAuthorization(AdminPolicies.TenantUser)
    .RequireAdminTenant()
    .RequireRateLimiting(RateLimits.Writes);
tenantAdmin.MapAccessTenantAdminEndpoints();
tenantAdmin.MapAdminAuditEndpoints();
tenantAdmin.MapStoresAdminEndpoints();
tenantAdmin.MapCatalogTenantAdminEndpoints();
tenantAdmin.MapInventoryTenantAdminEndpoints();

var storeAdmin = tenantAdmin.MapGroup("/stores/{storeId:guid}").RequireAdminStore();
storeAdmin.MapStoresStoreAdminEndpoints();
storeAdmin.MapProviderConnections();
storeAdmin.MapCatalogStoreAdminEndpoints();
storeAdmin.MapOrdersStoreAdminEndpoints();
storeAdmin.MapAdminOutboxEndpoints();
storeAdmin.MapAdminSuppressionEndpoints();

// Outside Development the schema is migrated by the pipeline before a new revision starts (D-074).
if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateDatabaseAsync();
    await app.Services.CreateFileStorageContainerAsync();
    var demo = await app.Services.SeedDevelopmentStoresAsync();
    await app.Services.SeedDevelopmentUsersAsync(demo.TenantId);
    await app.Services.SeedDevelopmentPlatformUsersAsync();
    await app.Services.SeedDevelopmentCatalogAsync(demo.TenantId, demo.WoodenHomeStoreId, demo.VoltElectronicsStoreId);
    await app.Services.SeedDevelopmentMethodsAsync(demo.TenantId, [demo.WoodenHomeStoreId, demo.VoltElectronicsStoreId]);
}

await app.RunAsync();

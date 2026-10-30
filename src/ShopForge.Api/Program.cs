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
using ShopForge.Api.EndToEnd;
using ShopForge.Api.Errors;
using ShopForge.Api.Frontends;
using ShopForge.Api.Health;
using ShopForge.Api.Messaging;
using ShopForge.Api.Provisioning;
using ShopForge.Api.Security;
using ShopForge.Catalog;
using ShopForge.Catalog.Development;
using ShopForge.Customers;
using ShopForge.Infrastructure;
using ShopForge.Infrastructure.Email;
using ShopForge.Infrastructure.Files;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Inventory;
using ShopForge.Orders;
using ShopForge.Orders.Development;
using ShopForge.Platform;
using ShopForge.Platform.Development;
using ShopForge.Platform.Users;
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

// Mail that never leaves looks exactly like mail that does, until a customer waits for a confirmation that will not
// come. Outside development and the test hosts, a deployment that would only log its mail refuses to start (D-205).
if (!builder.Environment.IsDevelopment()
    && !builder.Environment.IsEnvironment("Testing")
    && !builder.Environment.IsEndToEnd()
    && string.Equals(builder.Configuration["Email:Provider"] ?? EmailOptions.LogProvider, EmailOptions.LogProvider, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Email:Provider is '{EmailOptions.LogProvider}', which sends nothing. A deployment has to name a provider that delivers mail.");
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

// Named windows, and a ceiling under every one of them but the providers' webhooks. The ceiling is the important
// one: an endpoint nobody remembered to limit is still limited, which is the failure this stage exists to prevent
// (D-126).
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

    // Four a second for a minute, which is somebody typing quickly and correcting what they typed, and a starting point
    // to be measured (D-227).
    options.AddPolicy(RateLimits.Typing, context =>
        Window(context, "typing", builder.Configuration.GetValue("RateLimiting:Typing:PermitLimit", 240)));

    // Sized from what the providers document rather than from a shopper: three times the thousand a minute one
    // address must be able to send, and every provider retries what this refuses (D-196).
    options.AddPolicy(RateLimits.Webhooks, context =>
        Window(context, "webhooks", builder.Configuration.GetValue("RateLimiting:Webhooks:PermitLimit", 3000)));

    // A webhook's own window is wider than the ceiling, so counting it here as well would make the ceiling its
    // limit again.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/health") ? RateLimitPartition.GetNoLimiter("health")
        : IsWebhook(context) ? RateLimitPartition.GetNoLimiter("webhooks")
        : Window(context, "all", builder.Configuration.GetValue("RateLimiting:Global:PermitLimit", 600)));

    static bool IsWebhook(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == RateLimits.Webhooks;

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
builder.Services.AddStoresModule(builder.Configuration);
// Outside Development the browser always reaches the platform over TLS terminated at the edge (D-075). The setting
// exists so a test host can serve plain HTTP; a deployment leaves it alone.
var requireSecureCookies = builder.Configuration.GetValue("Security:RequireSecureCookies", !builder.Environment.IsDevelopment());
builder.Services.AddAccessModule(requireSecureCookies);
builder.Services.AddCatalogModule();
builder.Services.AddCustomersModule(requireSecureCookies);
builder.Services.AddInventoryModule();
builder.Services.AddOrdersModule();
builder.Services.AddPlatformModule(requireSecureCookies);

// A deployment for browser journeys and nothing else, which refuses to start pointed at anything real (D-203).
if (builder.Environment.IsEndToEnd())
{
    builder.Services.AddEndToEnd(builder.Configuration);
}

// The three sign-in schemes set this for their own cookies; this sets it for every cookie, including the cart's,
// which is a bearer capability for somebody's basket and was the one that did not (D-192). A policy rather than a
// line at each call site, so a cookie nobody remembers to mark is marked anyway.
builder.Services.Configure<CookiePolicyOptions>(options =>
    options.Secure = requireSecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest);

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ShopForgeDbContext>("database", tags: [HealthEndpoints.ReadinessTag])
    .AddCheck<FileStorageHealthCheck>("file-storage", tags: [HealthEndpoints.ReadinessTag])
    .AddCheck<SecretStoreHealthCheck>("secret-store", tags: [HealthEndpoints.ReadinessTag])
    .AddCheck<SchemaHealthCheck>("schema", tags: [HealthEndpoints.ReadinessTag]);

var app = builder.Build();

// A release migrates the schema with its own image, as a job of its own, before its revision serves (D-205). Run with
// `migrate`, the build applies what the database lacks and exits instead of serving.
if (args is ["migrate"])
{
    var applied = await app.Services.MigrateDatabaseAsync();
    app.Logger.LogInformation(
        "Applied {Count} migration(s){Migrations}.",
        applied.Count,
        applied.Count == 0 ? "; the database already had every one" : ": " + string.Join(", ", applied));

    // The first operator the environment names, invited on the admin's host while the platform has nobody (D-208). The
    // running app's outbox delivers the invitation. One that cannot be addressed stops the release before it serves.
    var firstOperator = app.Configuration["Platform:FirstOperator"];

    if (!string.IsNullOrWhiteSpace(firstOperator))
    {
        var adminHost = app.Configuration["Frontends:AdminHost"];
        var invitation = await app.Services.InviteFirstOperatorAsync(
            firstOperator,
            string.IsNullOrWhiteSpace(adminHost) ? null : new Uri($"https://{adminHost}"),
            app.Services.GetRequiredService<TimeProvider>());

        if (invitation.Refused)
        {
            app.Logger.LogError("Nobody was invited: {Refusal}", invitation.Outcome);
            Environment.ExitCode = 1;
        }
        else
        {
            app.Logger.LogInformation("{Outcome}", invitation.Outcome);
        }
    }

    await app.DisposeAsync();

    return;
}

app.UseEdgeHeaders();
app.UseCookiePolicy();
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
app.UseSchemaGate();
app.UseFrontends();
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
app.MapGroup(string.Empty).RequireStore().MapCatalogSiteRootEndpoints();

app.MapGroup("/api/payments").RequireRateLimiting(RateLimits.Webhooks).MapPaymentWebhookEndpoints();
app.MapEmailWebhookEndpoints();

// Administering ShopForge itself: outside tenancy, behind its own cookie (D-103).
var platform = app.MapGroup("/api/platform").RequireRateLimiting(RateLimits.Writes);
platform.MapPlatformAuthEndpoints();
var platformOperator = platform.MapGroup(string.Empty).RequireAuthorization(PlatformPolicies.PlatformUser);
platformOperator.MapStoresPlatformEndpoints();
platformOperator.MapOrdersPlatformEndpoints();
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
storeAdmin.MapAdminSendingDomainEndpoints();
storeAdmin.MapAdminOperationEndpoints();

// Outside Development the schema is migrated before a new revision starts, by the release's own job (D-074, D-205).
if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateDatabaseAsync();
    await app.Services.CreateFileStorageContainerAsync();
    var demo = await app.Services.SeedDevelopmentStoresAsync();
    await app.Services.SeedDevelopmentUsersAsync(demo.TenantId);
    await app.Services.SeedDevelopmentPlatformUsersAsync();
    await app.Services.SeedDevelopmentCatalogAsync(demo.TenantId, demo.WoodenHomeStoreId, demo.VoltElectronicsStoreId);
    await app.Services.SeedDevelopmentToolShopAsync(demo.TenantId, demo.ToolShopStoreId);
    await app.Services.SeedDevelopmentMethodsAsync(demo.TenantId, [demo.WoodenHomeStoreId, demo.VoltElectronicsStoreId, demo.ToolShopStoreId]);
}
else if (app.Environment.IsEndToEnd())
{
    app.MapEndToEnd();
    await app.PrepareEndToEndAsync();
}

await app.RunAsync();

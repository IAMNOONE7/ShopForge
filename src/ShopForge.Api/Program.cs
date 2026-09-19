using Microsoft.AspNetCore.HttpLogging;
using ShopForge.Access;
using ShopForge.Access.Development;
using ShopForge.Api.Health;
using ShopForge.Infrastructure;
using ShopForge.Infrastructure.Files;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores;
using ShopForge.Stores.Development;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
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

builder.Services.AddInfrastructure(builder.Configuration, [StoresModule.Assembly, AccessModule.Assembly]);
builder.Services.AddStoresModule();
builder.Services.AddAccessModule();

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

var admin = app.MapGroup("/api/admin");
admin.MapAccessAdminEndpoints();

var tenantAdmin = app.MapGroup("/api/admin")
    .RequireAuthorization(AdminPolicies.TenantUser)
    .RequireAdminTenant();
tenantAdmin.MapStoresAdminEndpoints();

if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateDatabaseAsync();
    await app.Services.CreateFileStorageContainerAsync();
    var demoTenantId = await app.Services.SeedDevelopmentStoresAsync();
    await app.Services.SeedDevelopmentUsersAsync(demoTenantId);
}

await app.RunAsync();

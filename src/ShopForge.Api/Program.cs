using Microsoft.AspNetCore.HttpLogging;
using ShopForge.Api.Health;
using ShopForge.Infrastructure;
using ShopForge.Infrastructure.Persistence;
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

builder.Services.AddInfrastructure(builder.Configuration, [StoresModule.Assembly]);
builder.Services.AddStoresModule();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ShopForgeDbContext>("database", tags: [HealthEndpoints.ReadinessTag]);

var app = builder.Build();

app.UseHttpLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseStoreResolution();

app.MapHealthEndpoints();

var storefront = app.MapGroup("/api/storefront").RequireStore();
storefront.MapStoresStorefrontEndpoints();

if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateDatabaseAsync();
    await app.Services.SeedDevelopmentStoresAsync();
}

await app.RunAsync();

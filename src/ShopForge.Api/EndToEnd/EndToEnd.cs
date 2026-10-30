using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using ShopForge.Catalog.Development;
using ShopForge.Infrastructure.Files;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Platform.Development;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Security;

namespace ShopForge.Api.EndToEnd;

// A deployment that exists to be driven by browser journeys: a database thrown away after the run, the providers
// replaced by the test rig on this machine, and secrets kept in memory. Nothing here is registered in any other
// environment, and this environment refuses to start pointed at anything real — no vault, no Stripe, and every
// provider address on this machine — so a journey can never take real money or send real mail (D-203).
internal static class EndToEnd
{
    public const string EnvironmentName = "E2E";

    public static bool IsEndToEnd(this IHostEnvironment environment) => environment.IsEnvironment(EnvironmentName);

    public static void AddEndToEnd(this IServiceCollection services, IConfiguration configuration)
    {
        RefuseAnythingReal(configuration);
        services.Replace(ServiceDescriptor.Singleton<ISecretStore, InMemorySecretStore>());

        var clock = new ShiftedClock();
        services.AddSingleton(clock);
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
    }

    // The schema and the storage container, as Development makes them, and the one operator the rig signs in as.
    // There is no demo catalogue: every run brings its own company and stores.
    public static async Task PrepareEndToEndAsync(this WebApplication app)
    {
        var configuration = app.Configuration;

        await app.Services.MigrateDatabaseAsync();
        await app.Services.CreateFileStorageContainerAsync();
        await app.Services.SeedPlatformOperatorAsync(
            Required(configuration, "EndToEnd:Operator:Email"), Required(configuration, "EndToEnd:Operator:Password"));
    }

    // Open, because the rig calls them before anybody exists to sign in as; there is nothing behind them but this
    // run's throwaway data, and the stack binds the API to this machine alone.
    public static void MapEndToEnd(this WebApplication app)
    {
        var endToEnd = app.MapGroup("/api/e2e");
        var startedAt = DateTimeOffset.UtcNow;
        var database = new NpgsqlConnectionStringBuilder(app.Configuration.GetConnectionString("ShopForge")).Database;

        // What the rig checks before it writes anything: that it is talking to an end-to-end API, and to the one
        // whose database its oracle reads.
        endToEnd.MapGet("/", () => TypedResults.Ok(new EndToEndMarker(EnvironmentName, database, startedAt)));

        // Time moved on for the whole stack — every run on it — so what waits for a reservation to run out, or for a
        // payment to go unanswered, does not wait for real.
        endToEnd.MapGet("/clock", (ShiftedClock clock) => TypedResults.Ok(new ClockReading(clock.GetUtcNow(), clock.Offset)));
        endToEnd.MapPost("/clock", Results<Ok<ClockReading>, ValidationProblem> (ClockChange change, ShiftedClock clock) =>
            clock.TryAdvance(TimeSpan.FromMinutes(change.AdvanceMinutes))
                ? TypedResults.Ok(new ClockReading(clock.GetUtcNow(), clock.Offset))
                : TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["advanceMinutes"] = [$"The clock moves forward only, and no more than {ShiftedClock.Furthest.TotalDays} days in all."],
                }));

        // The development data's Czech tool shop, for a run that asks for it: the rig makes the tree and the attributes
        // through the admin API and uploads the workbook to the import, as a merchant would, so a run's tool shop is
        // the one the search's tests and the development data have (D-227). Nothing here writes anything.
        endToEnd.MapGet("/catalogues/tool-shop", () => TypedResults.Ok(new ToolShopDescription(ToolShopCatalogue.Categories, ToolShopCatalogue.Attributes)));
        endToEnd.MapGet("/catalogues/tool-shop/products.xlsx", () => TypedResults.File(
            DevelopmentToolShop.ProductsWorkbook(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "tool-shop.xlsx"));

        // A pass run now rather than when it falls due, so a journey that waits for the sweep does not wait a minute.
        endToEnd.MapPost("/passes/{name}", async Task<Results<NoContent, NotFound>> (
            string name, IEnumerable<IScheduledPass> passes, CancellationToken cancellationToken) =>
        {
            if (passes.FirstOrDefault(pass => pass.Name == name) is not { } pass)
            {
                return TypedResults.NotFound();
            }

            await pass.RunAsync(cancellationToken);

            return TypedResults.NoContent();
        });
    }

    internal static void RefuseAnythingReal(IConfiguration configuration)
    {
        if (configuration["ProviderSecrets:VaultUri"] is { Length: > 0 })
        {
            throw new InvalidOperationException("The end-to-end environment keeps secrets in memory; a vault is configured.");
        }

        if (configuration["Payments:Stripe:SecretKey"] is { Length: > 0 })
        {
            throw new InvalidOperationException("The end-to-end environment takes no real payments; Stripe keys are configured.");
        }

        OnThisMachine(configuration, "Payments:Comgate:BaseAddress");
        OnThisMachine(configuration, "Shipping:Packeta:WidgetAddress");

        if (!string.Equals(configuration["Email:Provider"], "log", StringComparison.OrdinalIgnoreCase))
        {
            OnThisMachine(configuration, "Email:Mailgun:BaseUrl");
        }
    }

    private static void OnThisMachine(IConfiguration configuration, string key)
    {
        if (!Uri.TryCreate(configuration[key], UriKind.Absolute, out var address) || !address.IsLoopback)
        {
            throw new InvalidOperationException($"The end-to-end environment calls nothing outside this machine; {key} is '{configuration[key]}'.");
        }
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"The end-to-end environment needs {key}.");
}

internal sealed record EndToEndMarker(string Environment, string? Database, DateTimeOffset StartedAt);

internal sealed record ClockReading(DateTimeOffset Now, TimeSpan Offset);

internal sealed record ClockChange(double AdvanceMinutes);

internal sealed record ToolShopDescription(IReadOnlyList<ToolShopCategory> Categories, IReadOnlyList<ToolShopAttribute> Attributes);

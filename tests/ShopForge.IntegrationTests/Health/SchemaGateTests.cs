using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using ShopForge.Infrastructure.Persistence;
using ShopForge.IntegrationTests.Shipping;
using ShopForge.Shared.Tenancy;

namespace ShopForge.IntegrationTests.Health;

// A release migrates before its new revision serves; this is what still holds when it does not. A build whose
// database lacks its migrations answers nothing but its health, is not ready and does no background work, and once
// the migrations are in it serves without a restart. A database ahead of the build, as the previous image finds it
// after a rollback, is not behind (D-205).
public sealed class SchemaGateTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_build_whose_schema_is_behind_serves_nothing_but_its_health_until_it_catches_up()
    {
        await using var database = await OwnDatabase.CreateAsync(factory);
        await database.MigrateAsync(database.Migrations[^2]);
        var logs = new RecordingLogs();
        await using var behind = HostOn(database, builder => builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        using var client = behind.CreateClient();

        using var live = await client.GetAsync("/health/live", CancellationToken);
        using var version = await client.GetAsync("/health/version", CancellationToken);
        using var notReady = await client.GetAsync("/health/ready", CancellationToken);
        using var refused = await client.GetAsync("/api/storefront/store", CancellationToken);
        var problem = await refused.Content.ReadFromJsonAsync<ProblemView>(CancellationToken);

        await database.MigrateAsync();
        using var ready = await client.GetAsync("/health/ready", CancellationToken);
        using var served = await client.GetAsync("/api/storefront/store", CancellationToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (live.StatusCode, version.StatusCode));
        Assert.Equal((HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable), (notReady.StatusCode, refused.StatusCode));
        Assert.Equal(TimeSpan.FromSeconds(5), refused.Headers.RetryAfter?.Delta);
        Assert.Equal("This build is waiting for its database to be migrated.", problem!.Title);
        Assert.Contains(logs.Messages, message => message.Contains(database.Migrations[^1], StringComparison.Ordinal));

        // Served, by store resolution, which knows no store at this address: the gate has stepped aside.
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NotFound), (ready.StatusCode, served.StatusCode));
    }

    [Fact]
    public async Task A_database_ahead_of_the_build_is_not_behind()
    {
        await using var database = await OwnDatabase.CreateAsync(factory);
        await database.MigrateAsync();
        await database.ExecuteAsync(
            "INSERT INTO \"__EFMigrationsHistory\" (migration_id, product_version) VALUES ('29991231235959_FromALaterRelease', '10.0.13')");
        await using var rolledBack = HostOn(database);
        using var client = rolledBack.CreateClient();

        using var ready = await client.GetAsync("/health/ready", CancellationToken);
        using var served = await client.GetAsync("/api/storefront/store", CancellationToken);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NotFound), (ready.StatusCode, served.StatusCode));
    }

    // Not the outbox, not the scheduled passes, not its gauges: an empty database would have failed them all.
    [Fact]
    public async Task A_build_does_no_background_work_against_a_schema_it_was_not_built_for()
    {
        await using var database = await OwnDatabase.CreateAsync(factory);
        var logs = new RecordingLogs();
        await using var empty = HostOn(database, builder =>
        {
            builder.UseSetting("Schedules:Enabled", "true");
            builder.UseSetting("Outbox:IntervalSeconds", "1");
            builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(logs));
        });
        using var client = empty.CreateClient();

        using var live = await client.GetAsync("/health/live", CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(4), CancellationToken);
        var measured = MeasureOutbox(empty);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Empty(measured);
        Assert.Contains(logs.Messages, message => message.Contains("migration(s) behind this build", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, message =>
            message.Contains("The outbox run failed", StringComparison.Ordinal) || message.Contains("pass failed", StringComparison.Ordinal));
    }

    // What a release's migration job runs: the build itself, told to migrate. It applies what is missing and exits,
    // and the second time finds nothing to do.
    [Fact]
    public async Task The_build_migrates_its_database_when_told_to_and_exits()
    {
        await using var database = await OwnDatabase.CreateAsync(factory);

        var first = await MigrateCommandAsync(database);
        var pendingAfter = await database.PendingAsync();
        var second = await MigrateCommandAsync(database);

        Assert.Equal(0, first.ExitCode);
        Assert.Contains($"Applied {database.Migrations.Count} migration(s)", first.Output, StringComparison.Ordinal);
        Assert.Empty(pendingAfter);
        Assert.Equal(0, second.ExitCode);
        Assert.Contains("the database already had every one", second.Output, StringComparison.Ordinal);
    }

    // The outbox's gauges as an exporter would read them, from this host's meters alone.
    private static List<string> MeasureOutbox(WebApplicationFactory<Program> host)
    {
        var meters = host.Services.GetRequiredService<IMeterFactory>();
        var measured = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Scope == meters && instrument.Name.StartsWith("shopforge.outbox.", StringComparison.Ordinal))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => measured.Add(instrument.Name));
        listener.Start();
        listener.RecordObservableInstruments();

        return measured;
    }

    private WebApplicationFactory<Program> HostOn(OwnDatabase database, Action<IWebHostBuilder>? configure = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:ShopForge", database.ConnectionString);
            configure?.Invoke(builder);
        });

    private async Task<(int ExitCode, string Output)> MigrateCommandAsync(OwnDatabase database)
    {
        var start = new ProcessStartInfo("dotnet", [Path.Combine(AppContext.BaseDirectory, "ShopForge.Api.dll"), "migrate"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["ConnectionStrings__ShopForge"] = database.ConnectionString;
        start.Environment["ConnectionStrings__FileStorage"] = factory.FileStorageConnectionString;

        using var process = Process.Start(start)!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var errors = process.StandardError.ReadToEndAsync(deadline.Token);

        // A build that serves instead of exiting fails here rather than hanging the suite.
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!CancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);

            return (-1, "The build did not exit within two minutes.");
        }

        return (process.ExitCode, await output + await errors);
    }

    private sealed record ProblemView(string Title);

    // A database of this test's own on the suite's server, the model the application has pointed at it, and gone
    // again afterwards.
    private sealed class OwnDatabase : IAsyncDisposable
    {
        private readonly ShopForgeApiFactory _factory;
        private readonly string _name;

        private OwnDatabase(ShopForgeApiFactory factory, string name)
        {
            _factory = factory;
            _name = name;
            ConnectionString = new NpgsqlConnectionStringBuilder(factory.DatabaseConnectionString) { Database = name }.ConnectionString;
            Context = new ShopForgeDbContext(
                new DbContextOptionsBuilder<ShopForgeDbContext>().UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention().Options,
                new StoreContext(),
                factory.Services.GetRequiredService<EntityConfigurationAssemblies>());
            Migrations = [.. Context.Database.GetMigrations()];
        }

        public string ConnectionString { get; }

        public IReadOnlyList<string> Migrations { get; }

        private ShopForgeDbContext Context { get; }

        public static async Task<OwnDatabase> CreateAsync(ShopForgeApiFactory factory)
        {
            var name = $"schema_{Guid.NewGuid():N}";
            await OnServerAsync(factory, $"CREATE DATABASE {name}");

            return new OwnDatabase(factory, name);
        }

        public Task MigrateAsync(string? target = null) => Context.GetService<IMigrator>().MigrateAsync(target, CancellationToken);

        public async Task<List<string>> PendingAsync() => [.. await Context.Database.GetPendingMigrationsAsync(CancellationToken)];

        public Task ExecuteAsync(string sql) => Context.Database.ExecuteSqlRawAsync(sql, CancellationToken);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await OnServerAsync(_factory, $"DROP DATABASE IF EXISTS {_name} WITH (FORCE)");
        }

        private static async Task OnServerAsync(ShopForgeApiFactory factory, string sql)
        {
            await using var connection = new NpgsqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync(CancellationToken);

            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }
    }
}

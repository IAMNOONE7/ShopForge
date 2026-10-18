using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Shared.Tenancy;

namespace ShopForge.IntegrationTests;

// A database of a test's own on the suite's server, the model the application has pointed at it, and gone again
// afterwards.
internal sealed class OwnDatabase : IAsyncDisposable
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

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

    public Task<List<string>> TextAsync(FormattableString sql) => Context.Database.SqlQuery<string>(sql).ToListAsync(CancellationToken);

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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Shared.Tenancy;

namespace ShopForge.IntegrationTests.Catalog;

// The one line of this change that could take a shop off the internet is the backfill: a product that came
// through the migration without a variant has no SKU, no weight and nothing to sell. Every other test starts
// from an empty database, where a backfill of nothing is indistinguishable from no backfill at all — so this
// one starts from a database that still has products of the old shape (D-134).
public sealed class VariantMigrationTests(ShopForgeApiFactory factory)
{
    private const string Before = "20260929064806_IdempotentRequests";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_product_from_before_variants_comes_through_as_a_product_with_one_form()
    {
        var name = $"migration_{Guid.NewGuid():N}";
        var tenantId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();

        await CreateDatabaseAsync(name);

        try
        {
            await using var dbContext = ContextFor(name);
            var migrator = dbContext.GetService<IMigrator>();

            // The catalog as it stood before this stage: a product carrying its own SKU, barcode and weight.
            await migrator.MigrateAsync(Before, CancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                "INSERT INTO catalog.products (id, tenant_id, sku, ean, weight_grams) VALUES ({0}, {1}, 'OLD-1', '8594000000001', 850)",
                [productId, tenantId],
                CancellationToken);

            await migrator.MigrateAsync(cancellationToken: CancellationToken);

            var variants = await dbContext.Database
                .SqlQueryRaw<string>(
                    "SELECT sku || ':' || coalesce(ean, '-') || ':' || coalesce(weight_grams, 0)::text || ':' || position::text AS \"Value\" "
                    + "FROM catalog.product_variants WHERE product_id = {0}",
                    productId)
                .ToListAsync(CancellationToken);

            Assert.Equal(["OLD-1:8594000000001:850:0"], variants);
        }
        finally
        {
            await DropDatabaseAsync(name);
        }
    }

    // The same model the application has, pointed at a database of this test's own.
    private ShopForgeDbContext ContextFor(string name) =>
        new(
            new DbContextOptionsBuilder<ShopForgeDbContext>()
                .UseNpgsql(ConnectionStringFor(name))
                .UseSnakeCaseNamingConvention()
                .Options,
            new StoreContext(),
            factory.Services.GetRequiredService<EntityConfigurationAssemblies>());

    private string ConnectionStringFor(string name) =>
        new NpgsqlConnectionStringBuilder(factory.DatabaseConnectionString) { Database = name }.ConnectionString;

    private async Task CreateDatabaseAsync(string name) => await RunOnServerAsync($"CREATE DATABASE {name}");

    private async Task DropDatabaseAsync(string name) => await RunOnServerAsync($"DROP DATABASE IF EXISTS {name} WITH (FORCE)");

    private async Task RunOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.DatabaseConnectionString);
        await connection.OpenAsync(CancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}

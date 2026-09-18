using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ShopForge.Infrastructure.Persistence;
using ShopForge.IntegrationTests;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(ShopForgeApiFactory))]

namespace ShopForge.IntegrationTests;

public sealed class ShopForgeApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _database.StartAsync();
        await Services.MigrateDatabaseAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ShopForge", _database.GetConnectionString());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}

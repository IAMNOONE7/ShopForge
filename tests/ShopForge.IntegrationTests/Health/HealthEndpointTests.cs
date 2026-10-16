using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;

namespace ShopForge.IntegrationTests.Health;

public sealed class HealthEndpointTests(ShopForgeApiFactory factory)
{
    private const string UnreachableDatabase = "Host=localhost;Port=1;Username=shopforge;Password=shopforge";

    [Fact]
    public async Task Liveness_returns_200()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_returns_200_when_database_is_reachable()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The commit the build was made from rides on its version, so a release can tell its own revision from the one it
    // replaced (D-205).
    [Fact]
    public async Task The_build_says_which_build_it_is()
    {
        using var client = factory.CreateClient();

        var version = await client.GetStringAsync("/health/version", TestContext.Current.CancellationToken);

        Assert.Equal(
            typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            version);
        Assert.Matches("^[0-9]+\\.[0-9]+\\.[0-9]+", version);
    }

    [Fact]
    public async Task Database_outage_fails_readiness_but_not_liveness()
    {
        await using var factoryWithoutDatabase = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:ShopForge", UnreachableDatabase));
        using var client = factoryWithoutDatabase.CreateClient();

        using var readiness = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        using var liveness = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
    }
}

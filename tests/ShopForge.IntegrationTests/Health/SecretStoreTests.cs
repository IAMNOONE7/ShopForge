using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Health;

// The store a deployment with no vault uses, reached the way a provider will reach it (D-139). What a vault adds
// cannot be proved here — there is no subscription — so what is proved is the seam, the fallback and the refusal.
public sealed class SecretStoreTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_deployment_with_no_vault_reads_what_it_was_configured_with()
    {
        await using var configured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ProviderSecrets:comgate-secret", "s3cret"));
        using var started = configured.CreateClient();

        var secrets = configured.Services.GetRequiredService<ISecretStore>();

        Assert.Equal("s3cret", await secrets.FindAsync("comgate-secret", CancellationToken));
        Assert.Null(await secrets.FindAsync("never-configured", CancellationToken));
    }

    // A provider whose credentials cannot be read must not be offered, and the instance must say it is not ready
    // rather than find out when somebody tries to pay.
    [Fact]
    public async Task An_unreachable_secret_store_fails_readiness_but_not_liveness()
    {
        await using var broken = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ISecretStore>(new UnreachableSecrets())));
        using var client = broken.CreateClient();

        using var readiness = await client.GetAsync("/health/ready", CancellationToken);
        using var liveness = await client.GetAsync("/health/live", CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
    }

    private sealed class UnreachableSecrets : ISecretStore
    {
        public Task<string?> FindAsync(string name, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The vault is not answering.");

        public Task SetAsync(string name, string value, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task ForgetAsync(string name, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}

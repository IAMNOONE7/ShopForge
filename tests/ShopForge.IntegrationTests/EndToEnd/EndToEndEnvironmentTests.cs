using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.EndToEnd;

// The end-to-end environment is a deployment that must never be a real one: it refuses to start pointed at anything
// off this machine, and its hooks exist nowhere else (D-203). The suite's own fakes stay in place for every host built
// here, so an end-to-end host shares the suite's mail and nothing leaves the process.
public sealed class EndToEndEnvironmentTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Payments:Comgate:BaseAddress", "https://payments.comgate.cz/")]
    [InlineData("Shipping:Packeta:BaseAddress", "https://www.zasilkovna.cz/api/")]
    [InlineData("Email:Mailgun:BaseUrl", "https://api.eu.mailgun.net")]
    [InlineData("ProviderSecrets:VaultUri", "https://shopforge-kv-prov.vault.azure.net/")]
    [InlineData("Payments:Stripe:SecretKey", "sk_live_made_up_for_this_test")]
    public void It_will_not_start_pointed_at_anything_real(string key, string value)
    {
        using var real = EndToEndHost(builder => builder.UseSetting(key, value));

        var refused = Assert.ThrowsAny<Exception>(() => real.Services);

        Assert.Contains("end-to-end environment", Innermost(refused).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_other_environment_has_its_hooks()
    {
        using var client = factory.CreateClient();

        using var marker = await client.GetAsync("/api/e2e", CancellationToken);
        using var clock = await client.PostAsJsonAsync("/api/e2e/clock", new { AdvanceMinutes = 60 }, CancellationToken);
        using var pass = await client.PostAsync("/api/e2e/passes/expired-orders", null, CancellationToken);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (marker.StatusCode, clock.StatusCode, pass.StatusCode));
    }

    // Booted through the real Program.cs: it names its database, keeps a merchant's secret saved through the admin —
    // which a deployment with no vault cannot — and moves its clock forward only.
    [Fact]
    public async Task It_names_its_database_keeps_secrets_and_moves_its_clock_forward_only()
    {
        using var endToEnd = EndToEndHost(_ => { });
        var store = await FurnitureStore.CreateAsync(factory);
        using var admin = await TestUsers.LoginAsync(endToEnd, await TestUsers.CreateAsync(factory.Services, store.Store.TenantId));
        using var client = endToEnd.CreateClient();

        var marker = await client.GetFromJsonAsync<Marker>("/api/e2e", CancellationToken);
        using var connected = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/provider-connections/comgate", new { MerchantId = "RUN-TEST", Environment = "Test", IsActive = true }, CancellationToken);
        using var kept = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.Store.StoreId}/provider-connections/comgate/secret", new { Secret = "kept-in-memory" }, CancellationToken);
        using var forward = await client.PostAsJsonAsync("/api/e2e/clock", new { AdvanceMinutes = 5 }, CancellationToken);
        using var backward = await client.PostAsJsonAsync("/api/e2e/clock", new { AdvanceMinutes = -1 }, CancellationToken);
        using var tooFar = await client.PostAsJsonAsync("/api/e2e/clock", new { AdvanceMinutes = 31 * 24 * 60 }, CancellationToken);
        using var unknown = await client.PostAsync("/api/e2e/passes/no-such-pass", null, CancellationToken);
        var reading = await client.GetFromJsonAsync<ClockView>("/api/e2e/clock", CancellationToken);

        Assert.Equal("E2E", marker!.Environment);
        Assert.Equal(new NpgsqlConnectionStringBuilder(factory.DatabaseConnectionString).Database, marker.Database);
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NoContent), (connected.StatusCode, kept.StatusCode));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (forward.StatusCode, backward.StatusCode, tooFar.StatusCode));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(5), reading!.Offset);
    }

    // The suite's host made into an end-to-end one: the database and storage stay the suite's, and its operator is named
    // as the stack script names it.
    private WebApplicationFactory<Program> EndToEndHost(Action<IWebHostBuilder> configure) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("E2E");
            builder.UseSetting("ConnectionStrings:ShopForge", factory.DatabaseConnectionString);
            builder.UseSetting("EndToEnd:Operator:Email", "operator@e2e.test");
            builder.UseSetting("EndToEnd:Operator:Password", "E2e-made-up-for-this-test");
            configure(builder);
        });

    private static Exception Innermost(Exception exception) => exception.InnerException is { } inner ? Innermost(inner) : exception;

    private sealed record Marker(string Environment, string? Database);

    private sealed record ClockView(DateTimeOffset Now, TimeSpan Offset);
}

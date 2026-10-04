using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Payments;

// One company, several storefronts, a merchant account each. A payment taken for one must never travel through
// another's, and the credential behind either must not be readable from anywhere (D-138, D-139).
public sealed class ProviderConnectionTests : IDisposable
{
    private readonly ShopForgeApiFactory _factory;
    private readonly WebApplicationFactory<Program> _withGateway;

    public ProviderConnectionTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withGateway = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var gateway = new ConnectedGateway();
            services.AddSingleton<IPaymentProvider>(gateway);
            services.AddSingleton<IPaymentNotifications>(gateway);
            services.AddSingleton<ISecretStore>(new InMemorySecrets());
        }));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _withGateway.Dispose();

    [Fact]
    public async Task Two_storefronts_of_one_company_take_money_through_their_own_merchant()
    {
        var (first, second, admin) = await TwoStoresAsync();

        await ConnectAsync(admin, first, "merchant-first", "secret-of-the-first");
        await ConnectAsync(admin, second, "merchant-second", "secret-of-the-second");

        Assert.Equal("merchant-first", await MerchantOfAsync(first));
        Assert.Equal("merchant-second", await MerchantOfAsync(second));

        // And the credential behind each: one store's secret filed where another's would be read is the whole
        // failure this slice exists to prevent, and only differing values can tell it apart.
        Assert.Equal("secret-of-the-first", await CredentialOfAsync(first));
        Assert.Equal("secret-of-the-second", await CredentialOfAsync(second));
    }

    // The thing this slice exists to prevent: one store's connection answering for another.
    [Fact]
    public async Task A_store_that_has_not_connected_sees_nothing_of_the_one_that_has()
    {
        var (connected, unconnected, admin) = await TwoStoresAsync();
        await ConnectAsync(admin, connected, "merchant-first");

        var listed = await ConnectionsAsync(admin, unconnected);

        Assert.Empty(listed);
        Assert.Null(await MerchantOfAsync(unconnected));
    }

    // A gateway a merchant has not finished connecting is not offered at checkout: better no method than one
    // that cannot take money.
    [Fact]
    public async Task A_method_whose_merchant_is_not_connected_is_not_offered()
    {
        var (connected, unconnected, admin) = await TwoStoresAsync();
        await MethodAsync(admin, connected);
        await MethodAsync(admin, unconnected);
        await ConnectAsync(admin, connected, "merchant-first");

        Assert.Contains(ConnectedGateway.ProviderKey, await OfferedAsync(connected));
        Assert.DoesNotContain(ConnectedGateway.ProviderKey, await OfferedAsync(unconnected));
    }

    // A connection with no credential behind it is not finished, whatever the merchant id says.
    [Fact]
    public async Task A_connection_without_a_credential_is_not_enough()
    {
        var (store, _, admin) = await TwoStoresAsync();
        await MethodAsync(admin, store);

        using var saved = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.StoreId}/provider-connections/{ConnectedGateway.ProviderKey}",
            new { MerchantId = "merchant-first", Environment = "Test", IsActive = true },
            CancellationToken);
        var beforeSecret = await OfferedAsync(store);

        await SecretAsync(admin, store);
        var afterSecret = await OfferedAsync(store);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain(ConnectedGateway.ProviderKey, beforeSecret);
        Assert.Contains(ConnectedGateway.ProviderKey, afterSecret);
    }

    // The credential goes in and comes out of nowhere.
    [Fact]
    public async Task A_credential_is_never_read_back()
    {
        var (store, _, admin) = await TwoStoresAsync();
        await ConnectAsync(admin, store, "merchant-first");

        using var listed = await admin.GetAsync($"/api/admin/stores/{store.StoreId}/provider-connections", CancellationToken);
        var body = await listed.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
        Assert.Contains("\"hasSecret\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_provider_this_deployment_does_not_have_cannot_be_connected()
    {
        var (store, _, admin) = await TwoStoresAsync();

        using var refused = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.StoreId}/provider-connections/not-a-provider",
            new { MerchantId = "merchant-first", Environment = "Test", IsActive = true },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    private const string Secret = "the-merchants-own-secret";

    private async Task<(TestStore First, TestStore Second, HttpClient Admin)> TwoStoresAsync()
    {
        var (first, second) = await TestStores.CreateTwoStoresOfOneTenantAsync(_factory.Services);
        var user = await TestUsers.CreateAsync(_factory.Services, first.TenantId);

        return (first, second, await TestUsers.LoginAsync(_withGateway, user));
    }

    private async Task ConnectAsync(HttpClient admin, TestStore store, string merchantId, string? secret = null)
    {
        using var saved = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.StoreId}/provider-connections/{ConnectedGateway.ProviderKey}",
            new { MerchantId = merchantId, Environment = "Test", IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        await SecretAsync(admin, store, secret);
    }

    private async Task SecretAsync(HttpClient admin, TestStore store, string? secret = null)
    {
        using var kept = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.StoreId}/provider-connections/{ConnectedGateway.ProviderKey}/secret",
            new { Secret = secret ?? Secret },
            CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, kept.StatusCode);
    }

    private async Task MethodAsync(HttpClient admin, TestStore store)
    {
        using var method = await admin.PostAsJsonAsync(
            $"/api/admin/stores/{store.StoreId}/payment-methods",
            new { Name = "Gateway", ProviderKey = ConnectedGateway.ProviderKey, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, method.StatusCode);
    }

    private async Task<List<ConnectionView>> ConnectionsAsync(HttpClient admin, TestStore store) =>
        (await admin.GetFromJsonAsync<List<ConnectionView>>(
            $"/api/admin/stores/{store.StoreId}/provider-connections", CancellationToken))!;

    // What the gateway's own adapter would resolve for the store it is serving.
    private async Task<string?> MerchantOfAsync(TestStore store)
    {
        await using var scope = TestStores.CreateScope(_withGateway.Services, store);
        var connections = scope.ServiceProvider.GetRequiredService<IProviderConnections>();

        return (await connections.FindAsync(ConnectedGateway.ProviderKey, CancellationToken))?.MerchantId;
    }

    // What the provider's own adapter would read: the connection names where its credential is kept, and the
    // secret store is asked for that name and no other.
    private async Task<string?> CredentialOfAsync(TestStore store)
    {
        await using var scope = TestStores.CreateScope(_withGateway.Services, store);
        var connection = await scope.ServiceProvider.GetRequiredService<IProviderConnections>()
            .FindAsync(ConnectedGateway.ProviderKey, CancellationToken);

        return await scope.ServiceProvider.GetRequiredService<ISecretStore>()
            .FindAsync(connection!.SecretName!, CancellationToken);
    }

    private async Task<List<string>> OfferedAsync(TestStore store)
    {
        using var shopper = new StorefrontApi(_withGateway, store);
        var methods = await shopper.GetJsonAsync<MethodsView>("/api/storefront/checkout/methods");

        return [.. methods.PaymentMethods.Select(method => method.Code == "gateway" ? ConnectedGateway.ProviderKey : method.Code)];
    }

    private sealed record ConnectionView(string Provider, string MerchantId, string Environment, bool IsActive, bool HasSecret);

    private sealed record MethodsView(List<MethodView> PaymentMethods);

    private sealed record MethodView(string Code, string Name);
}

// A gateway that only works where a merchant has connected one.
internal sealed class ConnectedGateway : FakeGateway
{
    public new const string ProviderKey = "connected-gateway";

    public override string Key => ProviderKey;

    public override bool NeedsConnection => true;
}

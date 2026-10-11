using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Payments;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Stores;

// A published shop can take an order, and publication is the gate that makes that true. Before this, the gate
// asked whether a payment method was switched on — not whether it was connected to an account, and a method
// with no connection is left out of the checkout silently, so a shop could go live offering the shopper no way
// to pay at all (D-190).
public sealed class StoreReadinessTests : IDisposable
{
    private readonly ShopForgeApiFactory _factory;
    private readonly WebApplicationFactory<Program> _withGateway;
    private readonly InMemorySecrets _secrets = new();

    public StoreReadinessTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withGateway = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var gateway = new ConnectedGateway();
            services.AddSingleton<IPaymentProvider>(gateway);
            services.AddSingleton<IPaymentNotifications>(gateway);
            services.AddSingleton<ISecretStore>(_secrets);
        }));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _withGateway.Dispose();

    // The acceptance criterion: a store with the gateway active but unconnected cannot publish, and the message
    // names the method so a merchant knows which one to deal with.
    [Fact]
    public async Task A_shop_whose_gateway_is_switched_on_but_unconnected_cannot_publish()
    {
        var world = await ReadyToPublishAsync();
        await ActivateGatewayAsync(world, "Card");

        var readiness = await ReadinessAsync(world);
        using var refused = await world.Admin.PostAsync($"/api/admin/stores/{world.StoreId}/publish", null, CancellationToken);
        var problems = (await refused.Content.ReadFromJsonAsync<PublishProblem>(CancellationToken))!.Problems;

        Assert.False(readiness.IsReady);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains(problems, problem => problem.Contains("\"Card\"", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("not connected to an account", StringComparison.Ordinal));

        // And the refusal is the same answer the merchant could already read, named by the same key.
        var failing = Assert.Single(readiness.Checks, check => check.Check == "orders.payment-connected");
        Assert.False(failing.IsReady);
        Assert.Equal("required", failing.Weight);
    }

    // I5's shape: the gate on a connection is that a secret *name* is recorded, and the secret itself can be
    // deleted from the vault behind its back. The name stays, the money still cannot move.
    [Fact]
    public async Task A_shop_whose_provider_secret_has_vanished_cannot_publish()
    {
        var world = await ReadyToPublishAsync();
        await ActivateGatewayAsync(world, "Card");
        await ConnectGatewayAsync(world);

        var connected = await ReadinessAsync(world);
        using var published = await world.Admin.PostAsync($"/api/admin/stores/{world.StoreId}/publish", null, CancellationToken);

        Assert.True(connected.IsReady);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        // Now the vault loses it, the way a deleted key or a database moved between deployments would.
        _secrets.Clear();

        var afterwards = await ReadinessAsync(world);
        var failing = Assert.Single(afterwards.Checks, check => check.Check == "orders.payment-connected");

        // The acceptance criterion's second half: an already-published store carries the problem on its
        // overview rather than failing a shopper's checkout.
        Assert.False(afterwards.IsReady);
        Assert.False(failing.IsReady);
        Assert.Contains("\"Card\"", failing.Problem!, StringComparison.Ordinal);
    }

    // A method whose provider needs no merchant account needs no connection: the manual bank transfer every
    // store starts with must not be caught by this.
    [Fact]
    public async Task A_method_that_needs_no_account_is_ready_without_one()
    {
        var world = await ReadyToPublishAsync();

        var readiness = await ReadinessAsync(world);
        using var published = await world.Admin.PostAsync($"/api/admin/stores/{world.StoreId}/publish", null, CancellationToken);

        Assert.True(readiness.IsReady);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.True(Assert.Single(readiness.Checks, check => check.Check == "orders.payment-connected").IsReady);
    }

    // Advice is not a refusal. The suite runs with the log e-mail provider, which sends nothing — the merchant
    // is told, and is not stopped, because that is the operator's configuration rather than theirs.
    [Fact]
    public async Task Advice_is_reported_and_does_not_stop_a_publication()
    {
        var world = await ReadyToPublishAsync();

        var readiness = await ReadinessAsync(world);
        using var published = await world.Admin.PostAsync($"/api/admin/stores/{world.StoreId}/publish", null, CancellationToken);
        var email = Assert.Single(readiness.Checks, check => check.Check == "platform.email");

        Assert.True(readiness.IsReady);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.False(email.IsReady);
        Assert.Equal("advised", email.Weight);
        Assert.Contains("will not reach anybody", email.Problem!, StringComparison.Ordinal);
    }

    // A listing taxed as a class the shop has priced nowhere relevant falls back to the rate on the listing,
    // which is lawful and almost certainly not what the merchant meant (D-187). Advice, named.
    [Fact]
    public async Task A_tax_class_with_no_rate_is_advice_naming_the_class()
    {
        var world = await ReadyToPublishAsync();

        using var created = await world.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{world.StoreId}/tax-classes", new { Name = "Books", Code = "books" }, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var pointed = await world.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{world.StoreId}/products/{world.ListingId}",
            new { Name = "Spade", Price = 19.90m, VatRate = 21m, IsVisible = true, SortOrder = 0, TaxClass = "books" },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, pointed.StatusCode);

        var readiness = await ReadinessAsync(world);
        var advice = Assert.Single(readiness.Checks, check => check.Check == "catalog.tax-classes");

        Assert.True(readiness.IsReady);
        Assert.False(advice.IsReady);
        Assert.Equal("advised", advice.Weight);
        Assert.Contains("books", advice.Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_check_is_named_and_the_draft_store_reports_what_it_is_missing()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(_factory.Services);
        using var admin = await TestUsers.LoginAsync(_withGateway, await TestUsers.CreateAsync(_factory.Services, existing.TenantId));
        var created = await NewDraftAsync(admin);

        var readiness = await ReadinessAsync(new ReadinessWorld(admin, created, Guid.Empty));

        Assert.False(readiness.IsReady);
        Assert.Equal(
            [
                "catalog.tax-classes",
                "catalog.visible-products",
                "orders.payment-connected",
                "orders.payment-method",
                "orders.shipping-connected",
                "orders.shipping-method",
                "platform.email",
                "platform.email-sender",
                "store.company",
                "store.logo",
                "store.primary-domain",
            ],
            readiness.Checks.Select(check => check.Check));

        // A brand-new draft has its platform subdomain, which is proved on sight (D-189), and nothing else.
        Assert.True(Assert.Single(readiness.Checks, check => check.Check == "store.primary-domain").IsReady);
        Assert.Contains(readiness.Checks, check => check.Check == "store.logo" && !check.IsReady);
    }

    private async Task<ReadinessWorld> ReadyToPublishAsync()
    {
        var (existing, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(_factory.Services);

        // Not `using`: the world owns it and the factory disposes what it handed out.
        var admin = await TestUsers.LoginAsync(_withGateway, await TestUsers.CreateAsync(_factory.Services, existing.TenantId));
        var storeId = await NewDraftAsync(admin);

        await UploadLogoAsync(admin, storeId);
        await ProvisioningTests.SetCompanyAsync(admin, storeId);
        var listingId = await admin.ListProductAsync(storeId, await admin.CreateProductAsync(), "Spade", 19.90m);

        return new ReadinessWorld(admin, storeId, listingId);
    }

    private async Task<Guid> NewDraftAsync(HttpClient admin)
    {
        using var response = await admin.PostAsJsonAsync(
            "/api/admin/stores",
            new
            {
                Name = "Garden Tools",
                HostName = TestStores.UniqueLabel("ready"),
                Currency = "CZK",
                Culture = "cs-CZ",
                Theme = new { PrimaryColor = "#123456", SecondaryColor = "#FFFFFF", BorderRadius = 6 },
            },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CreatedStore>(CancellationToken))!.Id;
    }

    private async Task ActivateGatewayAsync(ReadinessWorld world, string name)
    {
        using var response = await world.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{world.StoreId}/payment-methods",
            new { Name = name, ProviderKey = ConnectedGateway.ProviderKey, IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task ConnectGatewayAsync(ReadinessWorld world)
    {
        using var saved = await world.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{world.StoreId}/provider-connections/{ConnectedGateway.ProviderKey}",
            new { MerchantId = "merchant-ready", Environment = "Test", IsActive = true },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var kept = await world.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{world.StoreId}/provider-connections/{ConnectedGateway.ProviderKey}/secret",
            new { Secret = "secret-of-the-ready-shop" },
            CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, kept.StatusCode);
    }

    private async Task<Readiness> ReadinessAsync(ReadinessWorld world) =>
        (await world.Admin.GetFromJsonAsync<Readiness>(
            $"/api/admin/stores/{world.StoreId}/readiness", CancellationToken))!;

    private static async Task UploadLogoAsync(HttpClient admin, Guid storeId)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(AdminCatalogApi.PngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "logo.png");

        using var response = await admin.PutAsync($"/api/admin/stores/{storeId}/logo", form, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed record ReadinessWorld(HttpClient Admin, Guid StoreId, Guid ListingId);

    private sealed record CreatedStore(Guid Id);

    private sealed record Readiness(bool IsReady, List<ReadinessCheck> Checks);

    private sealed record ReadinessCheck(string Check, bool IsReady, string? Problem, string Weight);

    private sealed record PublishProblem(List<string> Problems);
}

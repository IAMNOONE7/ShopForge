using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Platform;

public sealed class PlatformTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Running_the_platform_needs_a_platform_user_and_nothing_else()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var operatorClient = await SignInAsync();
        using var tenantAdmin = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));
        using var anonymous = factory.CreateClient();

        using var asOperator = await operatorClient.GetAsync("/api/platform/tenants", CancellationToken);
        using var asTenantUser = await tenantAdmin.GetAsync("/api/platform/tenants", CancellationToken);
        using var asNobody = await anonymous.GetAsync("/api/platform/tenants", CancellationToken);

        // And the platform cookie is no use in a company's own admin.
        using var operatorInTheAdmin = await operatorClient.GetAsync("/api/admin/stores", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, asOperator.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asTenantUser.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asNobody.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, operatorInTheAdmin.StatusCode);
    }

    [Fact]
    public async Task The_platform_sees_a_tenant_and_what_it_uses()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await PlaceAndPayAsync(furniture, shopper);
        using var operatorClient = await SignInAsync();

        var tenants = await operatorClient.GetFromJsonAsync<List<PlatformTenantView>>("/api/platform/tenants", CancellationToken);
        var tenant = tenants!.Single(candidate => candidate.Id == furniture.Store.TenantId);

        Assert.Equal("Active", tenant.Status);
        Assert.Equal(2, Usage(tenant, "stores"));
        Assert.Equal(4, Usage(tenant, "products"));
        Assert.Equal(4, Usage(tenant, "listings"));
        Assert.Equal(1, Usage(tenant, "orders"));
        Assert.Equal(1, Usage(tenant, "users"));
    }

    // Orders, listings and customers belong to a store, not to a company, so counting them means counting the
    // company's stores. One tenant's numbers must never pick up another's (D-105).
    [Fact]
    public async Task What_one_tenant_uses_is_not_what_another_uses()
    {
        var quiet = await FurnitureStore.CreateAsync(factory);
        var busy = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, busy.Store);
        await PlaceAndPayAsync(busy, shopper);
        using var operatorClient = await SignInAsync();

        var tenants = await operatorClient.GetFromJsonAsync<List<PlatformTenantView>>("/api/platform/tenants", CancellationToken);
        var withoutOrders = tenants!.Single(candidate => candidate.Id == quiet.Store.TenantId);
        var withOne = tenants!.Single(candidate => candidate.Id == busy.Store.TenantId);

        Assert.Equal(0, Usage(withoutOrders, "orders"));
        Assert.Equal(1, Usage(withOne, "orders"));
        Assert.Equal(4, Usage(withoutOrders, "listings"));
        Assert.Equal(4, Usage(withOne, "listings"));
    }

    // The operator never knows the owner's password: taking a company on invites its first owner, who chooses one
    // when they accept (D-114).
    [Fact]
    public async Task A_tenant_taken_on_by_the_platform_is_reached_by_inviting_its_owner()
    {
        using var operatorClient = await SignInAsync();
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        const string password = "Brand-new-tenant-2026";

        using var created = await operatorClient.PostAsJsonAsync(
            "/api/platform/tenants",
            new { Name = "Newcomer Ltd", OwnerEmail = email },
            CancellationToken);
        var tenant = (await created.Content.ReadFromJsonAsync<PlatformTenantView>(CancellationToken))!;

        var token = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.LatestLinkFor(email)),
            link => link is not null,
            CancellationToken);

        using var owner = factory.CreateClient();
        using var accepted = await owner.PostAsJsonAsync(
            "/api/admin/invitations/accept", new { Token = token, Password = password }, CancellationToken);
        using var signedIn = await owner.PostAsJsonAsync("/api/admin/auth/login", new { Email = email, Password = password }, CancellationToken);
        using var stores = await owner.GetAsync("/api/admin/stores", CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(("Newcomer Ltd", "Active"), (tenant.Name, tenant.Status));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stores.StatusCode);
        Assert.Empty((await stores.Content.ReadFromJsonAsync<List<AdminStoreRow>>(CancellationToken))!);
    }

    [Fact]
    public async Task Suspending_a_tenant_closes_its_shops_and_its_admin_without_losing_anything()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var operatorClient = await SignInAsync();
        var user = await TestUsers.CreateAsync(factory.Services, furniture.Store.TenantId);
        using var admin = await TestUsers.LoginAsync(factory, user);

        using var open = await StorefrontAsync(furniture.Store.HostName);
        using var suspended = await operatorClient.PostAsync($"/api/platform/tenants/{furniture.Store.TenantId}/suspend", null, CancellationToken);
        using var closedShop = await StorefrontAsync(furniture.Store.HostName);
        using var sessionAfterwards = await admin.GetAsync("/api/admin/stores", CancellationToken);
        using var signInAfterwards = await SignInToAdminAsync(user);

        using var resumed = await operatorClient.PostAsync($"/api/platform/tenants/{furniture.Store.TenantId}/resume", null, CancellationToken);
        using var openAgain = await StorefrontAsync(furniture.Store.HostName);
        using var signInAgain = await SignInToAdminAsync(user);
        var products = await factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM catalog.store_products WHERE store_id = {furniture.Store.StoreId}")
            .SingleAsync(CancellationToken));

        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, closedShop.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sessionAfterwards.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, signInAfterwards.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openAgain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signInAgain.StatusCode);
        Assert.Equal(4, products);
    }

    [Fact]
    public async Task One_tenant_is_suspended_without_touching_another()
    {
        var suspend = await FurnitureStore.CreateAsync(factory);
        var leave = await FurnitureStore.CreateAsync(factory);
        using var operatorClient = await SignInAsync();

        using var suspended = await operatorClient.PostAsync($"/api/platform/tenants/{suspend.Store.TenantId}/suspend", null, CancellationToken);
        using var again = await operatorClient.PostAsync($"/api/platform/tenants/{suspend.Store.TenantId}/suspend", null, CancellationToken);
        using var closed = await StorefrontAsync(suspend.Store.HostName);
        using var untouched = await StorefrontAsync(leave.Store.HostName);

        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, closed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, untouched.StatusCode);
    }

    [Fact]
    public async Task Taking_on_a_tenant_needs_a_name_and_an_owner_who_can_be_reached()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var elsewhere = await TestUsers.CreateAsync(factory.Services, store.TenantId);
        using var operatorClient = await SignInAsync();

        using var response = await operatorClient.PostAsJsonAsync(
            "/api/platform/tenants",
            new { Name = " ", OwnerEmail = "not-an-address" },
            CancellationToken);
        var problem = await response.Content.ReadAsStringAsync(CancellationToken);

        // An address that already works on ShopForge would be invited to a company nobody could then reach.
        using var taken = await operatorClient.PostAsJsonAsync(
            "/api/platform/tenants",
            new { Name = "Second Job Ltd", OwnerEmail = elsewhere.Email },
            CancellationToken);
        var tenants = await operatorClient.GetFromJsonAsync<List<PlatformTenantView>>("/api/platform/tenants", CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("name", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ownerEmail", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.DoesNotContain(tenants!, candidate => candidate.Name == "Second Job Ltd");
    }

    private static int Usage(PlatformTenantView tenant, string name) =>
        tenant.Usage.Single(count => count.Name == name).Value;

    private Task<HttpClient> SignInAsync() => TestPlatformUsers.SignInAsync(factory, CancellationToken);

    private async Task<HttpResponseMessage> SignInToAdminAsync(TestUser user)
    {
        using var client = factory.CreateClient();

        return await client.PostAsJsonAsync("/api/admin/auth/login", new { user.Email, user.Password }, CancellationToken);
    }

    private async Task<HttpResponseMessage> StorefrontAsync(string hostName)
    {
        using var client = factory.CreateClient();

        return await client.GetAsync($"http://{hostName}/api/storefront/store", CancellationToken);
    }

    private async Task PlaceAndPayAsync(FurnitureStore furniture, StorefrontApi shopper)
    {
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items",
            new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
    }

    private sealed record PlatformTenantView(Guid Id, string Name, string Status, List<UsageView> Usage);

    private sealed record UsageView(string Name, int Value);

    private sealed record AdminStoreRow(Guid Id, string Name);
}

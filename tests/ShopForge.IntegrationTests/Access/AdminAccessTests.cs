using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using ShopForge.Access.Domain;

namespace ShopForge.IntegrationTests.Access;

public sealed class AdminAccessTests(ShopForgeApiFactory factory)
{
    [Fact]
    public async Task Login_starts_a_session_for_the_users_tenant()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var user = await TestUsers.CreateAsync(factory.Services, store.TenantId, TenantRole.CatalogManager);
        using var client = await TestUsers.LoginAsync(factory, user);

        var me = await client.GetFromJsonAsync<CurrentUser>("/api/admin/auth/me", TestContext.Current.CancellationToken);

        Assert.Equal(user.Email, me?.Email);
        Assert.Equal("CatalogManager", me?.Role);
        Assert.Equal(store.TenantId, me?.TenantId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invalid_credentials_are_rejected_without_revealing_which_part_was_wrong(bool userExists)
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var user = await TestUsers.CreateAsync(factory.Services, store.TenantId);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/admin/auth/login",
            new { Email = userExists ? user.Email : "nobody@example.test", Password = "wrong-password" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        Assert.Equal("Invalid e-mail or password", problem?.Title);
    }

    [Theory]
    [InlineData("/api/admin/auth/me")]
    [InlineData("/api/admin/stores")]
    public async Task Admin_endpoints_require_a_session(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var client = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, store.TenantId));

        using var logout = await client.PostAsync("/api/admin/auth/logout", null, TestContext.Current.CancellationToken);
        using var me = await client.GetAsync("/api/admin/auth/me", TestContext.Current.CancellationToken);

        Assert.True(logout.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Admin_lists_only_the_stores_of_its_own_tenant()
    {
        var (storeA, storeB) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var (foreignStore, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        using var client = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, storeA.TenantId));

        var stores = await client.GetFromJsonAsync<List<AdminStore>>("/api/admin/stores", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { storeA.StoreId, storeB.StoreId }.Order(), stores!.Select(store => store.Id).Order());
        Assert.DoesNotContain(stores!, store => store.Id == foreignStore.StoreId);
        Assert.Equal(storeA.HostName, stores!.Single(store => store.Id == storeA.StoreId).PrimaryHostName);
    }

    private sealed record CurrentUser(Guid Id, string Email, string Role, Guid TenantId);

    private sealed record AdminStore(Guid Id, string Name, string? PrimaryHostName);
}

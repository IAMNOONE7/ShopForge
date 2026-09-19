using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Access.Domain;
using ShopForge.Shared.Tenancy;

namespace ShopForge.IntegrationTests.Access;

public sealed class SessionTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Session_cookie_is_http_only_and_same_site_strict()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var user = await TestUsers.CreateAsync(factory.Services, store.TenantId);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            "/api/admin/auth/login",
            JsonContent.Create(new { user.Email, user.Password }),
            CancellationToken);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("shopforge_admin=", StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_existing_session()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var user = await TestUsers.CreateAsync(factory.Services, store.TenantId);
        using var client = await TestUsers.LoginAsync(factory, user);

        await ChangeUserAsync(user, entry => entry.Property(tenantUser => tenantUser.IsActive).CurrentValue = false);
        using var response = await client.GetAsync("/api/admin/stores", CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Role_changes_apply_to_existing_sessions()
    {
        var (store, _) = await TestStores.CreateTwoStoresOfOneTenantAsync(factory.Services);
        var user = await TestUsers.CreateAsync(factory.Services, store.TenantId, TenantRole.CatalogManager);
        using var client = await TestUsers.LoginAsync(factory, user);

        await ChangeUserAsync(user, entry => entry.Property(tenantUser => tenantUser.Role).CurrentValue = TenantRole.Support);
        using var response = await client.GetAsync("/api/admin/auth/me", CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task ChangeUserAsync(TestUser user, Action<EntityEntry<TenantUser>> change)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().SetTenant(user.TenantId);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var tenantUser = await dbContext.Set<TenantUser>().SingleAsync(candidate => candidate.Email == user.Email, CancellationToken);

        change(dbContext.Entry(tenantUser));
        await dbContext.SaveChangesAsync(CancellationToken);
    }
}

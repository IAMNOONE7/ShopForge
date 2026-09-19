using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Access.Domain;
using ShopForge.Shared.Tenancy;

namespace ShopForge.IntegrationTests;

internal sealed record TestUser(string Email, string Password, Guid TenantId);

internal static class TestUsers
{
    public static async Task<TestUser> CreateAsync(IServiceProvider services, Guid tenantId, TenantRole role = TenantRole.Owner)
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        const string password = "Correct-horse-battery-1";

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().SetTenant(tenantId);

        var user = new TenantUser(tenantId, email, role);
        user.SetPasswordHash(scope.ServiceProvider.GetRequiredService<IPasswordHasher<TenantUser>>().HashPassword(user, password));

        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Add(user);
        await dbContext.SaveChangesAsync();

        return new TestUser(email, password, tenantId);
    }

    public static async Task<HttpClient> LoginAsync(ShopForgeApiFactory factory, TestUser user)
    {
        var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/admin/auth/login", new { user.Email, user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return client;
    }
}

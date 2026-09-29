using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Platform.Domain;

namespace ShopForge.IntegrationTests;

internal sealed record TestOperator(string Email, string Password);

internal static class TestPlatformUsers
{
    public static async Task<TestOperator> CreateAsync(ShopForgeApiFactory factory, CancellationToken cancellationToken)
    {
        var account = new TestOperator($"operator-{Guid.NewGuid():N}@shopforge.test", "Runs-the-platform-2026");

        await using var scope = factory.Services.CreateAsyncScope();
        var user = new PlatformUser(account.Email);
        user.SetPasswordHash(scope.ServiceProvider.GetRequiredService<IPasswordHasher<PlatformUser>>().HashPassword(user, account.Password));

        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return account;
    }

    public static async Task<HttpClient> SignInAsync(ShopForgeApiFactory factory, CancellationToken cancellationToken)
    {
        var account = await CreateAsync(factory, cancellationToken);

        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/platform/auth/login", new { account.Email, account.Password }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return client;
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Platform.Domain;

namespace ShopForge.IntegrationTests;

internal static class TestPlatformUsers
{
    public static async Task<HttpClient> SignInAsync(ShopForgeApiFactory factory, CancellationToken cancellationToken)
    {
        var email = $"operator-{Guid.NewGuid():N}@shopforge.test";
        const string password = "Runs-the-platform-2026";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var user = new PlatformUser(email);
            user.SetPasswordHash(scope.ServiceProvider.GetRequiredService<IPasswordHasher<PlatformUser>>().HashPassword(user, password));

            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            dbContext.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/platform/auth/login", new { Email = email, Password = password }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return client;
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Customers;

public sealed class RateLimitTests(ShopForgeApiFactory factory)
{
    [Fact]
    public async Task Sign_in_attempts_are_throttled_per_caller()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:Authentication:PermitLimit", "3"));
        using var client = limited.CreateClient();
        var attempt = new { Email = "nobody@example.test", Password = "wrong-password-2026" };
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            using var response = await client.PostAsJsonAsync(
                $"http://{furniture.Store.HostName}/api/storefront/account/login",
                attempt,
                TestContext.Current.CancellationToken);

            statuses.Add(response.StatusCode);
        }

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests],
            statuses);
    }
}

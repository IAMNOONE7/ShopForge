using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;

namespace ShopForge.IntegrationTests.Security;

// A provider's webhook has a window of its own: wider than the ceiling every other caller shares, because a
// provider pushes from a few addresses, and still a window, because anybody can post to it (D-196).
public sealed class WebhookWindowTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // A ceiling of one would refuse the second of these; only the webhook's own window of two decides, and it
    // still refuses the third.
    [Theory]
    [InlineData("/api/payments/comgate")]
    [InlineData("/api/email/mailgun")]
    public async Task A_webhook_is_held_to_its_own_window_and_not_to_the_ceiling(string path)
    {
        using var limited = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Global:PermitLimit", "1");
            builder.UseSetting("RateLimiting:Webhooks:PermitLimit", "2");
        });
        using var client = limited.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await client.PostAsJsonAsync(path, new { merchant = "NOBODY", transId = "nothing" }, CancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses[..^1]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }
}

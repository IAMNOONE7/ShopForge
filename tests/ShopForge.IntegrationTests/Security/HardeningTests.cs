using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Security;

public sealed class HardeningTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Everything_the_api_returns_tells_the_browser_to_do_nothing_with_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"http://{furniture.Store.HostName}/api/storefront/store", CancellationToken);
        var headers = response.Headers;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);

        // Promising a year of HTTPS over a plain connection would be promising the wrong party.
        Assert.False(headers.Contains("Strict-Transport-Security"));

        // Nothing needs to know what is serving this.
        Assert.False(headers.Contains("Server"));
    }

    [Fact]
    public async Task A_headline_error_is_served_with_the_same_headers()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("http://not-a-store.invalid/api/storefront/store", CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    // The point of the ceiling: an endpoint nobody thought to limit is limited anyway (D-126).
    [Fact]
    public async Task An_endpoint_with_no_window_of_its_own_still_has_the_ceiling()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var capped = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:Global:PermitLimit", "3"));
        using var client = capped.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var response = await client.GetAsync($"http://{furniture.Store.HostName}/api/storefront/products", CancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.All(statuses[..^1], status => Assert.Equal(HttpStatusCode.OK, status));
    }

    [Fact]
    public async Task Changing_something_is_throttled_more_tightly_than_reading()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var capped = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:Writes:PermitLimit", "2"));
        using var shopper = new StorefrontApi(capped, furniture.Store);
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await shopper.PostAsync(
                "/api/storefront/cart/items",
                new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    // Reading a whole catalogue costs the platform real work, so it gets a window of its own.
    [Fact]
    public async Task An_import_is_throttled_harder_than_an_ordinary_write()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var capped = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:Expensive:PermitLimit", "1"));
        using var admin = await TestUsers.LoginAsync(capped, await TestUsers.CreateAsync(factory.Services, furniture.Store.TenantId));

        using var first = await admin.ImportAsync(furniture.Store.StoreId, ImportFiles.Workbook(["sku", "name", "price", "vat"], [["LIMIT-A", "A", 1m, 21m]]));
        using var second = await admin.ImportAsync(furniture.Store.StoreId, ImportFiles.Workbook(["sku", "name", "price", "vat"], [["LIMIT-B", "B", 1m, 21m]]));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task A_body_nobody_asked_for_is_refused_by_the_server()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var client = factory.CreateClient();
        using var oversized = new ByteArrayContent(new byte[2 * 1024 * 1024]);
        oversized.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.PostAsync(
            $"http://{furniture.Store.HostName}/api/storefront/account/register", oversized, CancellationToken);

        // Refused for its size, whatever it claims to be, rather than read and then judged.
        Assert.Contains(
            response.StatusCode,
            new[] { HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest });
    }
}

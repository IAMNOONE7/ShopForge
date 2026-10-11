using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Security;

// Every cookie the application sets is marked Secure in a deployment. The sign-in schemes set it for their own;
// the cart's cookie — a bearer capability for somebody's basket — did not, and CodeQL said so on its first run.
// It is now a policy over every cookie, so one nobody remembers to mark is marked anyway (D-192).
public sealed class SecureCookieTests(ShopForgeApiFactory factory) : IDisposable
{
    // A host configured the way a deployment is: cookies secure whatever the request arrived over.
    private readonly WebApplicationFactory<Program> _deployed = factory.WithWebHostBuilder(builder =>
        builder.UseSetting("Security:RequireSecureCookies", "true"));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _deployed.Dispose();

    [Fact]
    public async Task The_cart_cookie_is_secure_in_a_deployment()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var client = _deployed.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/storefront/cart/items")
        {
            Content = JsonContent.Create(new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 }),
        };
        request.Headers.Host = furniture.Store.HostName;

        using var response = await client.SendAsync(request, CancellationToken);
        var cart = Assert.Single(response.Headers.GetValues("Set-Cookie"), header => header.StartsWith("shopforge_cart=", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("secure", cart, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cart, StringComparison.OrdinalIgnoreCase);
    }

    // And not on a development machine, which serves plain HTTP: a Secure cookie there would never be sent back,
    // and the cart would empty itself on every request.
    [Fact]
    public async Task The_cart_cookie_is_not_forced_secure_over_plain_http_in_development()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/storefront/cart/items")
        {
            Content = JsonContent.Create(new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 }),
        };
        request.Headers.Host = furniture.Store.HostName;

        using var response = await client.SendAsync(request, CancellationToken);
        var cart = Assert.Single(response.Headers.GetValues("Set-Cookie"), header => header.StartsWith("shopforge_cart=", StringComparison.Ordinal));

        Assert.DoesNotContain("secure", cart, StringComparison.OrdinalIgnoreCase);
    }
}

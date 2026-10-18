using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.Shared.Security;

namespace ShopForge.IntegrationTests.Diagnostics;

// Behind Front Door the store is decided by a header, so it matters a great deal who is allowed to set it (D-075).
public sealed class EdgeHeaderTests(ShopForgeApiFactory factory) : IDisposable
{
    private const string FrontDoorId = "11111111-2222-3333-4444-555555555555";

    private readonly WebApplicationFactory<Program> _behindEdge = factory.WithWebHostBuilder(builder =>
    {
        builder.AsDeployed();
        builder.UseSetting("Edge:FrontDoorId", FrontDoorId);
        builder.UseSetting("Security:RequireSecureCookies", "false");
    });

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_forwarded_host_from_the_edge_picks_the_store()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var response = await SendAsync(furniture.Store.HostName, FrontDoorId);
        var store = await response.Content.ReadFromJsonAsync<StoreView>(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(furniture.Store.Name, store!.Name);
    }

    // Container Apps cannot keep everybody but Front Door out of the app, so the app does: a request without this
    // profile's id did not come through the edge, and is refused rather than answered without its headers (D-207).
    [Fact]
    public async Task A_request_that_did_not_come_through_the_edge_is_refused()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var withoutId = await SendAsync(furniture.Store.HostName, profileId: null);
        using var withWrongId = await SendAsync(furniture.Store.HostName, "99999999-9999-9999-9999-999999999999");

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (withoutId.StatusCode, withWrongId.StatusCode));
    }

    // The platform probes the container itself, not through the edge, and a container it cannot probe it restarts.
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task The_health_checks_answer_the_platform_directly(string path)
    {
        using var client = _behindEdge.CreateClient();

        using var probed = await client.GetAsync(path, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, probed.StatusCode);
    }

    // A deployment does not set this; the environment decides it (Program.cs). What is worth guarding is that both
    // sessions follow the same rule — forgetting one of the two schemes would be easy and quiet.
    [Fact]
    public void Both_sessions_follow_the_secure_cookie_rule()
    {
        using var deployed = factory.WithWebHostBuilder(builder =>
        {
            builder.AsDeployed();
            builder.UseSetting("Security:RequireSecureCookies", "true");
        });
        var options = deployed.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        Assert.Equal(CookieSecurePolicy.Always, options.Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.Always, options.Get(CustomerPolicies.Scheme).Cookie.SecurePolicy);
    }

    public void Dispose() => _behindEdge.Dispose();

    private async Task<HttpResponseMessage> SendAsync(string storeHost, string? profileId)
    {
        using var client = _behindEdge.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://edge.invalid/api/storefront/store");
        request.Headers.Add("X-Forwarded-Host", storeHost);
        request.Headers.Add("X-Forwarded-Proto", "https");

        if (profileId is not null)
        {
            request.Headers.Add("X-Azure-FDID", profileId);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private sealed record StoreView(string Name);
}

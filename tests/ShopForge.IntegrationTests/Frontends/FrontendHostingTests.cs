using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ShopForge.IntegrationTests.Frontends;

// The two built frontends as the app serves them (D-206), from a stand-in for what the image carries: each app's page
// for its own routes, its hashed assets kept for a year, nothing the API or a missing file answers turned into a
// page, and each page with a policy of its own.
public sealed class FrontendHostingTests : IDisposable
{
    private const string AdminHost = "admin.platform.test";
    private const string EdgeHost = "shopforge-a1b2.z01.azurefd.test";
    private const string StorefrontPage = "<!doctype html><html><head><script type=\"module\" src=\"/assets/index-S1.js\"></script></head><body><div id=\"root\"></div></body></html>";
    private const string AdminPage = "<!doctype html><html><head><script type=\"module\" src=\"/admin/assets/index-A1.js\"></script></head><body><div id=\"root\"></div></body></html>";

    private readonly ShopForgeApiFactory _factory;
    private readonly DirectoryInfo _built = Directory.CreateTempSubdirectory("shopforge-frontends-");
    private readonly WebApplicationFactory<Program> _serving;

    public FrontendHostingTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        Write("storefront/index.html", StorefrontPage);
        Write("storefront/assets/index-S1.js", "console.log('shop');");
        Write("storefront/favicon.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        Write("admin/index.html", AdminPage);
        Write("admin/assets/index-A1.js", "console.log('admin');");
        _serving = factory.WithWebHostBuilder(builder => builder.UseSetting("Frontends:Root", _built.FullName));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _serving.Dispose();
        _built.Delete(recursive: true);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/p/oak-chair")]
    [InlineData("/category/chairs?page=2")]
    public async Task Every_storefront_route_is_the_storefront_s_page_asked_for_again_each_time(string path)
    {
        using var page = await GetAsync(_serving, "shop.example.test", path);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Equal(StorefrontPage, await page.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("no-cache", Header(page, "Cache-Control"));
        Assert.Contains("script-src 'self' https://widget.packeta.com;", Header(page, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", Header(page, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Equal("strict-origin-when-cross-origin", Header(page, "Referrer-Policy"));
        Assert.Equal("nosniff", Header(page, "X-Content-Type-Options"));
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/")]
    [InlineData("/admin/orders/2026-00012")]
    public async Task Every_admin_route_is_the_admin_s_page_with_a_stricter_policy(string path)
    {
        using var page = await GetAsync(_serving, "localhost", path);
        var policy = Header(page, "Content-Security-Policy");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(AdminPage, await page.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("no-cache", Header(page, "Cache-Control"));
        Assert.Contains("script-src 'self';", policy, StringComparison.Ordinal);
        Assert.Contains("style-src 'self';", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("packeta", policy, StringComparison.Ordinal);
        Assert.Equal("no-referrer", Header(page, "Referrer-Policy"));
    }

    // Their names change when they change, so they are kept for good; a name that is not there is a 404, never a page,
    // or a browser would run HTML as a script and show nothing at all.
    [Theory]
    [InlineData("/assets/index-S1.js", "/assets/index-GONE.js")]
    [InlineData("/admin/assets/index-A1.js", "/admin/assets/index-GONE.js")]
    public async Task A_hashed_asset_is_kept_for_a_year_and_a_missing_one_is_not_a_page(string present, string missing)
    {
        using var asset = await GetAsync(_serving, "shop.example.test", present);
        using var gone = await GetAsync(_serving, "shop.example.test", missing);

        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains("javascript", asset.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        Assert.Equal("public, max-age=31536000, immutable", Header(asset, "Cache-Control"));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.NotEqual("text/html", gone.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_file_at_the_root_that_is_not_hashed_is_kept_for_an_hour()
    {
        using var icon = await GetAsync(_serving, "shop.example.test", "/favicon.svg");

        Assert.Equal(HttpStatusCode.OK, icon.StatusCode);
        Assert.Equal("public, max-age=3600", Header(icon, "Cache-Control"));
    }

    [Theory]
    [InlineData("/api/nothing-here")]
    [InlineData("/api/storefront/no-such-thing")]
    [InlineData("/health/nothing-here")]
    public async Task Nothing_the_api_does_not_answer_becomes_a_page(string path)
    {
        using var answer = await GetAsync(_serving, "shop.example.test", path);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.NotEqual("text/html", answer.Content.Headers.ContentType?.MediaType);
    }

    // The framework's file fallback answers only GET and HEAD; this keeps it that way.
    [Fact]
    public async Task A_page_is_only_ever_read()
    {
        using var client = _serving.CreateClient();

        using var posted = await client.PostAsync("http://shop.example.test/p/oak-chair", content: null, CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, posted.StatusCode);
        Assert.Equal(["GET", "HEAD"], posted.Content.Headers.Allow.Order());
    }

    // Not on a merchant's domain: no sign-in page there, and no admin session cookie set for it.
    [Fact]
    public async Task The_admin_is_served_on_the_platform_s_own_host_only()
    {
        await using var platform = _serving.WithWebHostBuilder(builder => builder.UseSetting("Frontends:AdminHost", AdminHost));

        using var onAShop = await GetAsync(platform, "shop.example.test", "/admin/orders");
        using var onThePlatform = await GetAsync(platform, AdminHost, "/admin/orders");
        using var platformRoot = await GetAsync(platform, AdminHost, "/");

        Assert.Equal(HttpStatusCode.NotFound, onAShop.StatusCode);
        Assert.Equal(AdminPage, await onThePlatform.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(HttpStatusCode.Redirect, platformRoot.StatusCode);
        Assert.Equal("/admin/", platformRoot.Headers.Location?.OriginalString);
    }

    // The edge's own host name needs no DNS of the platform's, so the admin is there before its domain is, and stays.
    [Fact]
    public async Task The_admin_is_served_on_the_edge_s_own_host_too()
    {
        await using var platform = _serving.WithWebHostBuilder(builder => builder
            .UseSetting("Frontends:AdminHost", AdminHost)
            .UseSetting("Frontends:EdgeHost", EdgeHost));

        using var onTheEdge = await GetAsync(platform, EdgeHost, "/admin/orders");
        using var edgeRoot = await GetAsync(platform, EdgeHost, "/");
        using var onAShop = await GetAsync(platform, "shop.example.test", "/admin/orders");

        Assert.Equal(AdminPage, await onTheEdge.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("/admin/", edgeRoot.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, onAShop.StatusCode);
    }

    // Development, and every host in this suite but these, have no built frontends: Vite serves them there.
    [Fact]
    public async Task Without_built_frontends_there_are_no_pages()
    {
        using var nothing = await GetAsync(_factory, "shop.example.test", "/p/oak-chair");

        Assert.Equal(HttpStatusCode.NotFound, nothing.StatusCode);
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplicationFactory<Program> host, string hostName, string path)
    {
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        return await client.GetAsync($"http://{hostName}{path}", CancellationToken);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : string.Empty;

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_built.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}

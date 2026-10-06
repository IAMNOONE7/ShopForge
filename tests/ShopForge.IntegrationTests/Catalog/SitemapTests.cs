using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;

namespace ShopForge.IntegrationTests.Catalog;

// A crawler can find every page a shop wants found, and is told what to leave alone (D-167).
public sealed class SitemapTests(ShopForgeApiFactory factory)
{
    private static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sitemap_names_the_home_page_the_categories_and_the_listings()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var document = await SitemapAsync(furniture);
        var locations = Locations(document);

        Assert.Equal(Sitemap + "urlset", document.Name);
        Assert.Contains($"https://{furniture.Store.HostName}/", locations);
        Assert.Contains($"https://{furniture.Store.HostName}/c/chairs", locations);
        Assert.Contains($"https://{furniture.Store.HostName}/p/oak-chair", locations);
        Assert.All(locations, location => Assert.StartsWith($"https://{furniture.Store.HostName}/", location, StringComparison.Ordinal));
    }

    // A listing the shop has hidden, or has asked search engines to leave alone, is not advertised.
    [Fact]
    public async Task A_hidden_listing_and_one_that_hides_itself_are_left_out()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await HideAsync(furniture, "beech-stool");
        await NoIndexAsync(furniture, "walnut-chair");

        var locations = Locations(await SitemapAsync(furniture));

        Assert.Contains($"https://{furniture.Store.HostName}/p/oak-chair", locations);
        Assert.DoesNotContain($"https://{furniture.Store.HostName}/p/beech-stool", locations);
        Assert.DoesNotContain($"https://{furniture.Store.HostName}/p/walnut-chair", locations);
    }

    [Fact]
    public async Task One_stores_sitemap_never_names_anothers_page()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var other = await FurnitureStore.CreateAsync(factory);

        var locations = Locations(await SitemapAsync(furniture));

        Assert.DoesNotContain(locations, location => location.Contains(other.Store.HostName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Robots_points_at_the_sitemap_and_keeps_crawlers_out_of_a_cart()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var robots = await RobotsAsync(furniture);

        Assert.Contains($"Sitemap: https://{furniture.Store.HostName}/api/storefront/sitemap.xml", robots, StringComparison.Ordinal);
        Assert.Contains("Disallow: /cart", robots, StringComparison.Ordinal);
        Assert.Contains("Disallow: /checkout", robots, StringComparison.Ordinal);
        Assert.Contains("Disallow: /account", robots, StringComparison.Ordinal);
        Assert.DoesNotContain("Disallow: /\n", robots, StringComparison.Ordinal);
    }

    // A shop that has said it is not ready asks to be left alone entirely, and has nothing to publish.
    [Fact]
    public async Task A_store_that_hides_itself_disallows_everything_and_has_no_sitemap()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await HideTheStoreAsync(furniture);

        var robots = await RobotsAsync(furniture);
        using var shopper = new Orders.StorefrontApi(factory, furniture.Store);
        using var sitemap = await shopper.GetAsync("/api/storefront/sitemap.xml");

        Assert.Equal("User-agent: *\nDisallow: /\n", robots);
        Assert.Equal(HttpStatusCode.NotFound, sitemap.StatusCode);
    }

    // Past the protocol's limit a sitemap becomes a list of sitemaps. The limit is configuration so this can
    // watch it happen without inventing fifty thousand products.
    [Fact]
    public async Task Past_the_limit_the_sitemap_becomes_a_list_of_sitemaps()
    {
        using var small = factory.WithWebHostBuilder(builder => builder.UseSetting("Seo:SitemapChunkSize", "2"));
        var furniture = await FurnitureStore.CreateAsync(factory);

        var index = await SitemapAsync(furniture, small);
        var first = await ChunkAsync(furniture, small, 1);
        var second = await ChunkAsync(furniture, small, 2);
        using var shopper = new Orders.StorefrontApi(small, furniture.Store);
        using var beyond = await shopper.GetAsync("/api/storefront/sitemap-99.xml");

        Assert.Equal(Sitemap + "sitemapindex", index.Name);
        Assert.All(
            Locations(index),
            location => Assert.StartsWith($"https://{furniture.Store.HostName}/api/storefront/sitemap-", location, StringComparison.Ordinal));
        Assert.Equal(2, Locations(first).Count);
        Assert.Equal(Sitemap + "urlset", first.Name);
        Assert.NotEqual(Locations(first), Locations(second));
        Assert.Equal(HttpStatusCode.NotFound, beyond.StatusCode);
    }

    private static List<string> Locations(XElement document) =>
        [.. document.Descendants(Sitemap + "loc").Select(element => element.Value)];

    private async Task<XElement> SitemapAsync(FurnitureStore furniture, WebApplicationFactory<Program>? host = null)
    {
        using var shopper = new Orders.StorefrontApi(host ?? factory, furniture.Store);
        using var response = await shopper.GetAsync("/api/storefront/sitemap.xml");

        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);

        // A validator complains about a document that opens straight into its root element.
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", body, StringComparison.Ordinal);

        return XDocument.Parse(body).Root!;
    }

    private async Task<XElement> ChunkAsync(FurnitureStore furniture, WebApplicationFactory<Program> host, int chunk)
    {
        using var shopper = new Orders.StorefrontApi(host, furniture.Store);
        using var response = await shopper.GetAsync($"/api/storefront/sitemap-{chunk}.xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return XDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).Root!;
    }

    private async Task<string> RobotsAsync(FurnitureStore furniture)
    {
        using var shopper = new Orders.StorefrontApi(factory, furniture.Store);

        return await shopper.GetStringAsync("/api/storefront/robots.txt");
    }

    private Task HideAsync(FurnitureStore furniture, string slug) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<StoreProduct>()
            .Where(listing => listing.Slug == slug)
            .ExecuteUpdateAsync(update => update.SetProperty(listing => listing.IsVisible, false), CancellationToken));

    private Task NoIndexAsync(FurnitureStore furniture, string slug) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<StoreProduct>()
            .Where(listing => listing.Slug == slug)
            .ExecuteUpdateAsync(update => update.SetProperty(listing => listing.SeoNoIndex, true), CancellationToken));

    private Task HideTheStoreAsync(FurnitureStore furniture) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Database.ExecuteSqlAsync(
            $"UPDATE stores.stores SET seo_no_index = true WHERE id = {furniture.Store.StoreId}",
            CancellationToken));
}

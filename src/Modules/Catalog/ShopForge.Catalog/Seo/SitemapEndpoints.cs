using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Stores;

namespace ShopForge.Catalog.Seo;

// What a crawler is told: every page this shop wants found, and what to leave alone. Both documents live here
// because the sitemap is almost entirely catalogue, and splitting one subject across two modules would be
// worse than one file in the module that holds most of it (D-167).
internal static class SitemapEndpoints
{
    // The limit in the sitemap protocol. Configurable so a test can watch the chunking happen without
    // inventing fifty thousand products.
    private const string ChunkSizeSetting = "Seo:SitemapChunkSize";
    private const int ProtocolLimit = 50_000;

    private static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";

    // A crawler comes back, and often. The window it shares with a shopper writing to their cart is the wrong
    // one for it (D-126), so these have their own.
    public static IEndpointRouteBuilder MapSeoDocuments(this IEndpointRouteBuilder storefront)
    {
        storefront.MapGet("/robots.txt", RobotsAsync);
        storefront.MapGet("/sitemap.xml", SitemapAsync);
        storefront.MapGet("/sitemap-{chunk:int}.xml", ChunkAsync);

        return storefront;
    }

    private static async Task<ContentHttpResult> RobotsAsync(
        DbContext dbContext,
        ICurrentStoreSettings storeSettings,
        IStoreUrls urls,
        CancellationToken cancellationToken)
    {
        var settings = await storeSettings.GetAsync(cancellationToken);
        var address = await urls.FindAsync(cancellationToken);

        // A shop that has said it is not ready, or that has no address of its own to be found at, asks to be
        // left alone entirely. An unpublished store never reaches this at all: its host does not resolve.
        if (settings.Seo.NoIndex || address is null)
        {
            return Text("User-agent: *\nDisallow: /\n");
        }

        // What no crawler should spend its time on: a cart and a checkout belong to one visitor, an account is
        // private, and a filtered list is the same products in another order.
        var lines = new List<string>
        {
            "User-agent: *",
            "Disallow: /cart",
            "Disallow: /checkout",
            "Disallow: /account",
            "Disallow: /order/",
            // Both positions of each, because a robots pattern is matched against the address as written: a
            // rule naming "?f." never matches "?page=2&f.material=oak", and a filter is as crawlable in the
            // second query parameter as in the first (D-174).
            "Disallow: /*?f.",
            "Disallow: /*&f.",
            "Disallow: /*?sort=",
            "Disallow: /*&sort=",
            string.Empty,
            $"Sitemap: https://{address.Host}/api/storefront/sitemap.xml",
        };

        return Text(string.Join('\n', lines) + "\n");
    }

    private static async Task<Results<ContentHttpResult, NotFound>> SitemapAsync(
        DbContext dbContext,
        ICurrentStoreSettings storeSettings,
        IStoreUrls urls,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (await PagesAsync(dbContext, storeSettings, urls, cancellationToken) is not { } pages)
        {
            return TypedResults.NotFound();
        }

        var chunkSize = ChunkSize(configuration);

        if (pages.Count <= chunkSize)
        {
            return Xml(UrlSet(pages));
        }

        // Past the limit the sitemap becomes a list of sitemaps. The chunks are numbered from one because that
        // is how they read in a server log.
        var chunks = (pages.Count + chunkSize - 1) / chunkSize;
        var index = new XElement(
            Sitemap + "sitemapindex",
            Enumerable.Range(1, chunks).Select(chunk => new XElement(
                Sitemap + "sitemap",
                new XElement(Sitemap + "loc", $"https://{pages[0].Host}/api/storefront/sitemap-{chunk}.xml"))));

        return Xml(index);
    }

    private static async Task<Results<ContentHttpResult, NotFound>> ChunkAsync(
        int chunk,
        DbContext dbContext,
        ICurrentStoreSettings storeSettings,
        IStoreUrls urls,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (chunk < 1 || await PagesAsync(dbContext, storeSettings, urls, cancellationToken) is not { } pages)
        {
            return TypedResults.NotFound();
        }

        var chunkSize = ChunkSize(configuration);
        var wanted = pages.Skip((chunk - 1) * chunkSize).Take(chunkSize).ToList();

        return wanted.Count == 0 ? TypedResults.NotFound() : Xml(UrlSet(wanted));
    }

    // Every page of this store a crawler should know about, in a stable order so that a chunk means the same
    // thing from one request to the next. Null when there is nothing to publish at all.
    private static async Task<List<Page>?> PagesAsync(
        DbContext dbContext,
        ICurrentStoreSettings storeSettings,
        IStoreUrls urls,
        CancellationToken cancellationToken)
    {
        var settings = await storeSettings.GetAsync(cancellationToken);
        var address = await urls.FindAsync(cancellationToken);

        if (settings.Seo.NoIndex || address is null)
        {
            return null;
        }

        // A listing the shop has hidden, or one it has told search engines to leave alone, is not advertised.
        var listings = await dbContext.Set<StoreProduct>()
            .AsNoTracking()
            .Where(listing => listing.IsVisible && !listing.SeoNoIndex)
            .OrderBy(listing => listing.Slug)
            .Select(listing => listing.Slug)
            .ToListAsync(cancellationToken);

        var categories = await dbContext.Set<Category>()
            .AsNoTracking()
            .OrderBy(category => category.Slug)
            .Select(category => category.Slug)
            .ToListAsync(cancellationToken);

        return
        [
            new Page(address.Host, address.Home),
            .. categories.Select(slug => new Page(address.Host, address.Category(slug))),
            .. listings.Select(slug => new Page(address.Host, address.Product(slug))),
        ];
    }

    private static int ChunkSize(IConfiguration configuration) =>
        Math.Clamp(configuration.GetValue(ChunkSizeSetting, ProtocolLimit), 1, ProtocolLimit);

    private static XElement UrlSet(List<Page> pages) => new(
        Sitemap + "urlset",
        pages.Select(page => new XElement(Sitemap + "url", new XElement(Sitemap + "loc", page.Location))));

    // No row in the catalogue records when it last changed, so `lastmod` is left out rather than guessed at.
    // A crawler treats an absent one as unknown, which is the truth.
    //
    // The declaration is written by hand because XDocument.ToString leaves it out, and a sitemap that opens
    // straight into its root element is one a validator complains about.
    private static ContentHttpResult Xml(XElement document) =>
        TypedResults.Text(
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n{document}",
            "application/xml",
            System.Text.Encoding.UTF8);

    private static ContentHttpResult Text(string body) =>
        TypedResults.Text(body, "text/plain", System.Text.Encoding.UTF8);

    private sealed record Page(string Host, string Location);
}

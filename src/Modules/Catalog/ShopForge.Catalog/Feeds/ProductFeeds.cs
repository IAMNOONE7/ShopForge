using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Feeds.Google;
using ShopForge.Shared.Feeds;
using ShopForge.Shared.Files;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Shipping;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Feeds;

// Producing a feed, storing it and recording how it went. One of these for every shape of feed there will ever
// be, because what differs between them is the writing and nothing else (D-168).
internal sealed class ProductFeeds(
    DbContext dbContext,
    IStoreContext storeContext,
    IEnumerable<IProductFeedFormat> formats,
    ICurrentStoreSettings storeSettings,
    IStoreUrls urls,
    IStockLedger stock,
    IStoreShippingRates shipping,
    IFileStorage files,
    TimeProvider clock,
    ILogger<ProductFeeds> logger) : IProductFeeds
{
    // Reads the same gathering the run uses, and writes nothing. Only Google has rules of its own so far; a
    // second format with its own will say so here rather than every caller learning both.
    public async Task<List<FeedProblem>?> CheckAsync(string feed, CancellationToken cancellationToken)
    {
        if (feed != GoogleMerchantFeed.FeedKey)
        {
            return null;
        }

        var address = await urls.FindAsync(cancellationToken);

        if (address is null)
        {
            return null;
        }

        var settings = await storeSettings.GetAsync(cancellationToken);
        var problems = new List<FeedProblem>();

        await foreach (var product in ProductsAsync(address, settings.Currency, _ => { }, () => { }, cancellationToken))
        {
            if (GoogleFeedChecks.Problems(product) is { Count: > 0 } found)
            {
                problems.Add(new FeedProblem(product.Sku, product.Name, found));
            }
        }

        return problems;
    }

    public async Task<FeedRun?> RunAsync(string feed, CancellationToken cancellationToken)
    {
        var format = formats.SingleOrDefault(candidate => candidate.Key == feed);
        var arrangement = await dbContext.Set<StoreFeed>().SingleOrDefaultAsync(candidate => candidate.Feed == feed, cancellationToken);

        if (format is null || arrangement is not { IsEnabled: true })
        {
            return null;
        }

        var address = await urls.FindAsync(cancellationToken);

        if (address is null)
        {
            // Every address in a feed is absolute, so a shop nobody can reach has nothing to publish (D-164).
            arrangement.Failed(clock.GetUtcNow(), "The store has no proved domain, so its products have no addresses to advertise.");
            await dbContext.SaveChangesAsync(cancellationToken);

            return null;
        }

        var settings = await storeSettings.GetAsync(cancellationToken);
        var started = clock.GetUtcNow();
        var skipped = 0;
        var written = 0;
        var path = PathOf(storeContext.StoreId!.Value, feed);

        try
        {
            // Written to a file and then stored, rather than streamed to storage, so a run that fails
            // part-way leaves yesterday's document in place rather than half of today's.
            var buffer = new MemoryStream();
            await format.WriteAsync(
                buffer,
                new FeedStore(
                    settings.Name,
                    address.Language,
                    settings.Currency,
                    address.Home,
                    await shipping.FindAsync(cancellationToken)),
                ProductsAsync(address, settings.Currency, count => written = count, () => skipped++, cancellationToken),
                cancellationToken);

            buffer.Position = 0;
            await files.SaveAsync(path, buffer, format.ContentType, cancellationToken);

            arrangement.Ran(
                path,
                clock.GetUtcNow(),
                (int)(clock.GetUtcNow() - started).TotalMilliseconds,
                buffer.Length,
                written,
                skipped);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Producing the {Feed} feed for store {StoreId} failed.", feed, storeContext.StoreId);
            arrangement.Failed(clock.GetUtcNow(), exception.Message);
            await dbContext.SaveChangesAsync(cancellationToken);

            return null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new FeedRun(arrangement.LastRunAt!.Value, arrangement.LastMilliseconds!.Value, arrangement.LastBytes!.Value, written, skipped);
    }

    // Everything on sale, with the identifiers a feed asks for. A listing with nothing in stock is still
    // advertised — an engine is told the availability rather than left to guess from an absence — but one the
    // shop has hidden, or asked search engines to leave alone, is not in a feed either (D-165).
    private async IAsyncEnumerable<FeedProduct> ProductsAsync(
        StoreAddress address,
        string currency,
        Action<int> counted,
        Action skipped,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var listings = await (
                from listing in dbContext.Set<StoreProduct>().AsNoTracking()
                join product in dbContext.Set<Product>().AsNoTracking() on listing.ProductId equals product.Id
                where listing.IsVisible && !listing.SeoNoIndex
                orderby listing.Slug
                select new
                {
                    listing.Slug,
                    listing.Name,
                    listing.Description,
                    listing.Price,
                    product.Brand,
                    Variants = product.Variants.OrderBy(variant => variant.Position).Select(variant => new
                    {
                        variant.Id,
                        variant.Sku,
                        variant.Ean,
                        variant.PartNumber,
                        variant.Condition,
                    }).ToList(),
                    ImageIds = product.Images.OrderBy(image => image.Position).Select(image => image.Id).ToList(),
                    listing.Id,
                    listing.ProductId,
                })
            .ToListAsync(cancellationToken);

        var available = await stock.AvailableAsync([.. listings.SelectMany(listing => listing.Variants.Select(variant => variant.Id))], cancellationToken);
        var written = 0;

        foreach (var listing in listings)
        {
            // One row per form of the thing, because that is what somebody buys and what a feed's identifiers
            // describe (D-134). A form with no SKU could not be matched back and is counted rather than sent.
            foreach (var variant in listing.Variants)
            {
                if (string.IsNullOrWhiteSpace(variant.Sku))
                {
                    skipped();

                    continue;
                }

                written++;

                var images = listing.ImageIds
                    .Select(imageId => address.Image($"/api/storefront/products/{listing.Id}/images/{imageId}"))
                    .ToList();

                yield return new FeedProduct(
                    variant.Sku,
                    listing.Name,
                    listing.Description,
                    listing.Price,
                    currency,
                    address.Product(listing.Slug),
                    images.FirstOrDefault(),
                    [.. images.Skip(1)],
                    listing.Brand,
                    variant.Ean,
                    variant.PartNumber,
                    variant.Condition?.ToString(),
                    available.GetValueOrDefault(variant.Id),
                    listing.ProductId.ToString(),
                    listing.Variants.Count > 1);
            }
        }

        counted(written);
    }

    // One path per shop and feed, overwritten each run: the current document is the only one anybody wants.
    internal static string PathOf(Guid storeId, string feed) => $"feeds/{storeId:n}/{feed}.xml";
}

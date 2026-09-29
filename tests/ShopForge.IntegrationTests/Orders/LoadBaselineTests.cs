using System.Diagnostics;
using System.Net;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Orders;

// A baseline rather than a load test: the same work, measured the same way on every build, so that the day
// somebody adds a query per row to the catalog it shows up here instead of in production (D-133).
//
// It measures the application, not a deployment — in-process, one caller at a time, against the suite's own
// PostgreSQL. The numbers it prints are worth comparing with the ones in PERFORMANCE.md; the ceilings it fails
// on are deliberately far above them, because a build machine's idea of a millisecond is nobody else's.
public sealed class LoadBaselineTests(ShopForgeApiFactory factory)
{
    private const int Products = 120;
    private const int Reads = 50;
    private const int Checkouts = 15;

    // What a page of the catalog and a product page cost in round trips today. They are exact rather than a
    // ceiling: a query that appears is worth looking at even when it is a cheap one.
    private const int CatalogQueries = 13;
    private const int ProductQueries = 7;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_storefront_stays_within_its_baseline()
    {
        var furniture = await BusyStoreAsync();
        using var shopper = new StorefrontApi(factory, furniture.Store);

        var listing = await MeasureAsync("catalog, first page", Reads, () => shopper.GetAsync("/api/storefront/products"));
        var deepPage = await MeasureAsync("catalog, fifth page", Reads, () => shopper.GetAsync("/api/storefront/products?page=5"));
        var filtered = await MeasureAsync("catalog, filtered", Reads, () => shopper.GetAsync("/api/storefront/products?material=oak"));
        var detail = await MeasureAsync("product page", Reads, () => shopper.GetAsync("/api/storefront/products/oak-chair"));
        var checkout = await CheckoutBaselineAsync(furniture);

        var queries = await QueriesAsync(furniture);

        Report(listing, deepPage, filtered, detail, checkout);

        // The threshold that means something. A page of the catalog talks to the database a fixed number of
        // times however many products are on it; the day that stops being true, this is what says so.
        Assert.Equal(CatalogQueries, queries.Catalog);
        Assert.Equal(ProductQueries, queries.Product);

        Assert.All([listing, deepPage, filtered, detail], scenario => Assert.True(
            scenario.Median < TimeSpan.FromMilliseconds(100),
            $"{scenario.Name} took {scenario.Median.TotalMilliseconds:F0} ms at the median, which is far above its baseline."));
        Assert.True(
            checkout.Median < TimeSpan.FromMilliseconds(200),
            $"{checkout.Name} took {checkout.Median.TotalMilliseconds:F0} ms at the median, which is far above its baseline.");
    }

    // The same two pages against a catalog of four products and one of a hundred and twenty: the number of
    // round trips must be the same, because it must not depend on how much a shop sells.
    private async Task<(int Catalog, int Product)> QueriesAsync(FurnitureStore furniture)
    {
        var small = await FurnitureStore.CreateAsync(factory);

        var catalog = await CountedAsync(furniture, "/api/storefront/products");
        var catalogOfFour = await CountedAsync(small, "/api/storefront/products");
        var product = await CountedAsync(furniture, "/api/storefront/products/oak-chair");
        var productOfFour = await CountedAsync(small, "/api/storefront/products/oak-chair");

        Assert.Equal(catalogOfFour, catalog);
        Assert.Equal(productOfFour, product);

        return (catalog, product);
    }

    private async Task<int> CountedAsync(FurnitureStore furniture, string path)
    {
        var tally = QueryCounter.NewTally();

        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var warmed = await shopper.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, warmed.StatusCode);

        using var counted = await shopper.GetAsync(path, (QueryCounter.HeaderName, tally));
        Assert.Equal(HttpStatusCode.OK, counted.StatusCode);

        return factory.Queries[tally];
    }

    // Only the checkout itself is timed: filling a cart is the shopper's own pace, not the shop's.
    private async Task<Scenario> CheckoutBaselineAsync(FurnitureStore furniture)
    {
        var carts = new List<StorefrontApi>();

        try
        {
            for (var shopper = 0; shopper < Checkouts; shopper++)
            {
                var client = new StorefrontApi(factory, furniture.Store);
                using var added = await client.PostAsync(
                    "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
                Assert.Equal(HttpStatusCode.OK, added.StatusCode);
                carts.Add(client);
            }

            var next = 0;

            return await MeasureAsync(
                "checkout",
                Checkouts,
                () => carts[next++].PostAsync("/api/storefront/checkout", Checkout.Request()),
                HttpStatusCode.Created);
        }
        finally
        {
            carts.ForEach(cart => cart.Dispose());
        }
    }

    private static async Task<Scenario> MeasureAsync(
        string name,
        int iterations,
        Func<Task<HttpResponseMessage>> request,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        // One run to warm the query plans and the JIT, so the first shopper of the day is not the measurement.
        using (var warmup = await request())
        {
            Assert.Equal(expected, warmup.StatusCode);
        }

        var taken = new List<TimeSpan>(iterations);

        for (var iteration = 1; iteration < iterations; iteration++)
        {
            var started = Stopwatch.GetTimestamp();
            using var response = await request();
            taken.Add(Stopwatch.GetElapsedTime(started));

            Assert.Equal(expected, response.StatusCode);
        }

        taken.Sort();

        return new Scenario(name, taken[taken.Count / 2], taken[(int)(taken.Count * 0.95)], taken.Count);
    }

    private static void Report(params Scenario[] scenarios)
    {
        var output = TestContext.Current.TestOutputHelper;

        output?.WriteLine($"{"scenario",-22}{"runs",6}{"median ms",12}{"p95 ms",10}");

        foreach (var scenario in scenarios)
        {
            output?.WriteLine(
                $"{scenario.Name,-22}{scenario.Runs,6}{scenario.Median.TotalMilliseconds,12:F1}{scenario.Slowest.TotalMilliseconds,10:F1}");
        }
    }

    // A catalog with enough in it that paging, filtering and counting are doing real work.
    private async Task<FurnitureStore> BusyStoreAsync()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        for (var index = 0; index < Products; index++)
        {
            var productId = await furniture.Admin.CreateProductAsync();
            await furniture.Admin.ListProductAsync(
                furniture.Store.StoreId, productId, $"Filler {index:000}", 10m + index, slug: $"filler-{index:000}");
        }

        return furniture;
    }

    private sealed record Scenario(string Name, TimeSpan Median, TimeSpan Slowest, int Runs);
}

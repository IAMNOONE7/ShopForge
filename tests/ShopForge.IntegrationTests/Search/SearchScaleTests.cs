using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Catalog.Search;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Search;

// What search costs on a catalogue nobody would call small. The rows go in with SQL rather than through the
// admin API: the point is the query that reads them, and twenty thousand round trips would measure the test
// harness instead (D-178).
//
// The cost is counted rather than timed, as a feed's is (D-133). A wall clock on a shared build machine
// measures the machine: the first version of this failed in CI at 2.9 seconds for a page that takes 30
// milliseconds here, with a narrow search and a broad one landing within 3% of each other — a figure made of
// waiting, not of work. A count does not move when the machine is busy, and it is what actually catches a
// search that started asking per row (D-184).
public sealed class SearchScaleTests(ShopForgeApiFactory factory)
{
    private const int Listings = 20_000;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Searching_a_catalogue_of_twenty_thousand_costs_what_searching_four_costs()
    {
        var small = await FurnitureStore.CreateAsync(factory);
        var large = await FurnitureStore.CreateAsync(factory);
        await FillAsync(large);

        // A word a few things have and a word most of them have: the number of round trips must not depend on
        // how many answers there are, any more than on how many products the shop sells.
        var forFour = await QueriesAsync(small, "chair");
        var forNarrow = await QueriesAsync(large, "chesterfield");
        var forBroad = await QueriesAsync(large, "filler");

        Assert.Equal(forFour, forNarrow);
        Assert.Equal(forFour, forBroad);
    }

    // The backstop, and nothing more. It is set where it is because a build machine's idea of a second is
    // nobody else's; what it catches is a search that has stopped using the index altogether, not a slow
    // afternoon on a shared runner.
    [Fact]
    public async Task A_catalogue_of_twenty_thousand_still_answers_in_a_reasonable_time()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await FillAsync(furniture);

        using var shopper = new StorefrontApi(factory, furniture.Store);
        await shopper.GetStringAsync("/api/storefront/products?q=chesterfield");

        var started = Stopwatch.GetTimestamp();
        await shopper.GetStringAsync("/api/storefront/products?q=chesterfield");
        var taken = Stopwatch.GetElapsedTime(started);

        TestContext.Current.TestOutputHelper?.WriteLine($"a search of {Listings:n0} listings took {taken.TotalMilliseconds:F0} ms");

        Assert.True(taken < TimeSpan.FromSeconds(30), $"a search took {taken.TotalMilliseconds:F0} ms, which is not a slow machine but a broken query");
    }

    private async Task<int> QueriesAsync(FurnitureStore furniture, string terms)
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var path = $"/api/storefront/products?q={terms}";

        // One run to warm the plans, as the load baseline does, then the one that is counted.
        using var warmed = await shopper.GetAsync(path);
        Assert.Equal(System.Net.HttpStatusCode.OK, warmed.StatusCode);

        var tally = QueryCounter.NewTally();
        using var counted = await shopper.GetAsync(path, (QueryCounter.HeaderName, tally));
        Assert.Equal(System.Net.HttpStatusCode.OK, counted.StatusCode);

        return factory.Queries[tally];
    }

    // One insert for the products and the listings, and one statement to index them — the same statement
    // production runs for a catalogue that pre-dates the column.
    private async Task FillAsync(FurnitureStore furniture)
    {
        await factory.QueryAsync(furniture.Store, async dbContext =>
            await dbContext.Database.ExecuteSqlAsync(
                $"""
                WITH made AS (
                    INSERT INTO catalog.products (id, tenant_id, sku, option_names)
                    SELECT gen_random_uuid(), {furniture.Store.TenantId}, 'SCALE-' || number, ARRAY[]::text[]
                    FROM generate_series(1, {Listings}) AS number
                    RETURNING id, sku
                )
                INSERT INTO catalog.store_products
                    (id, store_id, product_id, name, slug, description, price, vat_rate, is_visible, sort_order,
                     rating_average, rating_count, seo_no_index)
                SELECT gen_random_uuid(), {furniture.Store.StoreId}, made.id,
                       'Filler ' || made.sku, lower(made.sku),
                       CASE WHEN right(made.sku, 2) = '00' THEN 'A chesterfield among the filler.' ELSE 'Ordinary filler.' END,
                       10, 21, true, 0, 0, 0, false
                FROM made
                """,
                CancellationToken));

        await using var scope = TestStores.CreateScope(factory.Services, furniture.Store);
        await scope.ServiceProvider.GetRequiredService<SearchIndex>().RefreshStoreAsync(CancellationToken);
    }
}

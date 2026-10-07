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
public sealed class SearchScaleTests(ShopForgeApiFactory factory)
{
    private const int Listings = 20_000;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_catalogue_of_twenty_thousand_answers_a_search_within_its_budget()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await FillAsync(furniture);

        using var shopper = new StorefrontApi(factory, furniture.Store);

        // One run to warm the plan and the JIT, as the load baseline does.
        await shopper.GetStringAsync("/api/storefront/products?q=walnut");

        var narrow = await MeasureAsync(shopper, "a word a few listings have", "chesterfield");
        var broad = await MeasureAsync(shopper, "a word most listings have", "filler");

        // Far above what it should take, for the same reason the load baseline's ceilings are: a build
        // machine's idea of a millisecond is nobody else's. What this catches is a search that stopped using
        // the index.
        Assert.True(narrow < TimeSpan.FromMilliseconds(600), $"a narrow search took {narrow.TotalMilliseconds:F0} ms");
        Assert.True(broad < TimeSpan.FromMilliseconds(600), $"a broad search took {broad.TotalMilliseconds:F0} ms");
    }

    private static async Task<TimeSpan> MeasureAsync(StorefrontApi shopper, string what, string terms)
    {
        var taken = new List<TimeSpan>();

        for (var run = 0; run < 5; run++)
        {
            var started = Stopwatch.GetTimestamp();
            await shopper.GetStringAsync($"/api/storefront/products?q={terms}");
            taken.Add(Stopwatch.GetElapsedTime(started));
        }

        taken.Sort();
        TestContext.Current.TestOutputHelper?.WriteLine($"{what,-32}{taken[taken.Count / 2].TotalMilliseconds,10:F1} ms");

        return taken[taken.Count / 2];
    }

    // One insert for the listings and one statement to index them — the same statement production runs for a
    // catalogue that pre-dates the column.
    private async Task FillAsync(FurnitureStore furniture)
    {
        // A store lists each product once, so the filler needs products of its own as well as listings.
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

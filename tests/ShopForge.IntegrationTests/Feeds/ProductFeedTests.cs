using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.Shared.Feeds;

namespace ShopForge.IntegrationTests.Feeds;

// One way of producing, storing and serving a feed, with no particular feed's shape in it (D-168).
public sealed class ProductFeedTests : IDisposable
{
    private readonly ShopForgeApiFactory _factory;
    private readonly RecordingFeedFormat _format = new();
    private readonly WebApplicationFactory<Program> _withFeed;

    public ProductFeedTests(ShopForgeApiFactory factory)
    {
        _factory = factory;
        _withFeed = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IProductFeedFormat>(_format)));
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _withFeed.Dispose();

    [Fact]
    public async Task A_feed_is_produced_stored_and_served_back_byte_for_byte()
    {
        var world = await FeedStoreAsync();

        var run = await RunAsync(world);
        var collected = await CollectAsync(world, run.Url!);
        var text = System.Text.Encoding.UTF8.GetString(collected);

        // Bytes rather than characters: the document is UTF-8 and what was stored has to be what is served,
        // byte for byte, whatever any one of those bytes means.
        Assert.True(run.LastBytes > 0);
        Assert.Equal(run.LastBytes, collected.Length);
        Assert.Contains($"store=\"{world.Furniture.Store.Name}\"", text, StringComparison.Ordinal);
        Assert.Contains("<item sku=", text, StringComparison.Ordinal);
    }

    // Everything a format needs about one thing for sale, gathered once however many formats there are.
    [Fact]
    public async Task Every_form_of_every_listing_arrives_with_its_identifiers_and_an_absolute_address()
    {
        var world = await FeedStoreAsync();

        await RunAsync(world);
        var chair = _format.LastProducts.Single(product => product.Sku == FurnitureStore.SkuOf("oak-chair"));

        Assert.Equal("Oak Chair", chair.Name);
        Assert.Equal(100m, chair.Price);
        Assert.Equal($"https://{world.Furniture.Store.HostName}/p/oak-chair", chair.Url);
        Assert.Equal("8594000000006", chair.Gtin);
        Assert.Equal("EUR", chair.Currency);
        Assert.Equal("en", _format.LastStore!.Language);
    }

    // A listing the shop has hidden, or has asked search engines to leave alone, is not advertised either.
    [Fact]
    public async Task A_hidden_listing_is_not_in_the_feed()
    {
        var world = await FeedStoreAsync();
        using var hidden = await world.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{world.Furniture.Store.StoreId}/products/{world.Furniture.Products["beech-stool"]}",
            new { Name = "Beech Stool", Slug = "beech-stool", Description = (string?)null, Price = 50m, VatRate = 21m, IsVisible = false, SortOrder = 0 },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);

        await RunAsync(world);

        Assert.DoesNotContain(_format.LastProducts, product => product.Sku == FurnitureStore.SkuOf("beech-stool"));
    }

    [Fact]
    public async Task A_feed_a_store_has_switched_off_serves_nothing()
    {
        var world = await FeedStoreAsync();
        var run = await RunAsync(world);

        using var off = await world.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{world.Furniture.Store.StoreId}/feeds/stub", new { IsEnabled = false }, CancellationToken);
        using var collected = await world.Shopper.GetAsync(PathOf(run.Url!));

        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, collected.StatusCode);
    }

    // One engine's access is revoked without disturbing the others, which is why the token is per consumer.
    [Fact]
    public async Task A_rotated_token_stops_the_old_one_working()
    {
        var world = await FeedStoreAsync();
        var run = await RunAsync(world);
        var wasWorking = PathOf(run.Url!);

        using var rotated = await world.Admin.PostAsync(
            $"/api/admin/stores/{world.Furniture.Store.StoreId}/feeds/stub/token", null, CancellationToken);
        var now = await rotated.Content.ReadFromJsonAsync<FeedView>(CancellationToken);

        using var withTheOldOne = await world.Shopper.GetAsync(wasWorking);
        using var withTheNewOne = await world.Shopper.GetAsync(PathOf(now!.Url!));

        Assert.Equal(HttpStatusCode.Unauthorized, withTheOldOne.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withTheNewOne.StatusCode);
    }

    [Fact]
    public async Task A_collection_with_no_token_or_a_wrong_one_is_refused()
    {
        var world = await FeedStoreAsync();
        await RunAsync(world);

        using var none = await world.Shopper.GetAsync("/api/storefront/feeds/stub.xml");
        using var wrong = await world.Shopper.GetAsync("/api/storefront/feeds/stub.xml?token=not-the-token");

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    // A shop with a feed switched on but never produced has nothing to collect, which is not an error.
    [Fact]
    public async Task A_feed_never_produced_has_nothing_to_collect()
    {
        var world = await FeedStoreAsync(run: false);
        var feeds = await world.Admin.GetFromJsonAsync<List<FeedView>>(
            $"/api/admin/stores/{world.Furniture.Store.StoreId}/feeds", CancellationToken);

        using var collected = await world.Shopper.GetAsync(PathOf(feeds!.Single(feed => feed.Feed == "stub").Url!));

        Assert.Equal(HttpStatusCode.NotFound, collected.StatusCode);
    }

    [Fact]
    public async Task The_run_is_recorded_with_what_it_wrote_and_how_long_it_took()
    {
        var world = await FeedStoreAsync();

        var run = await RunAsync(world);

        Assert.NotNull(run.LastRunAt);
        Assert.True(run.LastMilliseconds >= 0);
        Assert.Equal(_format.LastProducts.Count, run.LastProducts);
        Assert.Equal(0, run.LastSkipped);
        Assert.Null(run.LastError);
    }

    // A run that fails leaves yesterday's document where it is: an engine reading old prices beats one
    // reading an error.
    [Fact]
    public async Task A_failed_run_is_recorded_and_leaves_the_last_good_document_alone()
    {
        var world = await FeedStoreAsync();
        var good = await RunAsync(world);
        var wasServed = await CollectAsync(world, good.Url!);

        _format.Fails = true;

        try
        {
            using var failed = await world.Admin.PostAsync(
                $"/api/admin/stores/{world.Furniture.Store.StoreId}/feeds/stub/run", null, CancellationToken);
            var feeds = await world.Admin.GetFromJsonAsync<List<FeedView>>(
                $"/api/admin/stores/{world.Furniture.Store.StoreId}/feeds", CancellationToken);
            var stillServed = await CollectAsync(world, good.Url!);

            Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
            Assert.NotNull(feeds!.Single(feed => feed.Feed == "stub").LastError);

            // Still there and still whole: the document is written in one go, so a format that gives up
            // part-way never reaches the stored copy.
            Assert.Equal(wasServed, stillServed);
        }
        finally
        {
            _format.Fails = false;
        }
    }

    [Fact]
    public async Task One_stores_feed_never_names_anothers_products()
    {
        var world = await FeedStoreAsync();
        var elsewhere = await FeedStoreAsync();

        await RunAsync(world);
        var theirs = _format.LastProducts.Select(product => product.Url).ToList();

        Assert.All(theirs, url => Assert.StartsWith($"https://{world.Furniture.Store.HostName}/", url, StringComparison.Ordinal));
        Assert.DoesNotContain(theirs, url => url.Contains(elsewhere.Furniture.Store.HostName, StringComparison.Ordinal));
    }

    // Once a day, through the housekeeping the platform already runs: a shopping engine collects on its own
    // schedule and reads whatever was produced last, so nothing has to line up with anybody's timing.
    [Fact]
    public async Task The_daily_housekeeping_produces_every_feed_a_shop_has_switched_on()
    {
        var world = await FeedStoreAsync(run: false);

        await _withFeed.Services.GetRequiredService<ShopForge.Infrastructure.Messaging.StoreMaintenance>().RunAsync(CancellationToken);
        var feeds = await world.Admin.GetFromJsonAsync<List<FeedView>>(
            $"/api/admin/stores/{world.Furniture.Store.StoreId}/feeds", CancellationToken);
        var stub = feeds!.Single(feed => feed.Feed == "stub");

        Assert.NotNull(stub.LastRunAt);
        Assert.True(stub.LastBytes > 0);
    }

    // What a feed of fifty thousand products would cost is the question, and a wall clock cannot answer it:
    // a query per product is a few milliseconds here and an outage there (D-133). So the count is what is
    // held, and it does not move when the catalogue grows.
    [Fact]
    public async Task Producing_a_feed_costs_the_same_however_many_products_there_are()
    {
        var small = await FeedStoreAsync(run: false);
        var large = await FeedStoreAsync(run: false);
        await AddProductsAsync(large, 40);

        var forFew = await QueriesToRunAsync(small);
        var forMany = await QueriesToRunAsync(large);

        Assert.Equal(forFew, forMany);
        Assert.True(_format.LastProducts.Count >= 40, $"the larger catalogue wrote {_format.LastProducts.Count} products");
    }

    private async Task<int> QueriesToRunAsync(FeedWorld world)
    {
        var tally = QueryCounter.NewTally();
        var path = $"/api/admin/stores/{world.Furniture.Store.StoreId}/feeds/stub/run";

        using var warmed = await world.Admin.PostAsync(path, null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, warmed.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(QueryCounter.HeaderName, tally);
        using var counted = await world.Admin.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, counted.StatusCode);

        return _factory.Queries[tally];
    }

    private async Task AddProductsAsync(FeedWorld world, int count)
    {
        for (var number = 0; number < count; number++)
        {
            var productId = await world.Admin.CreateProductAsync();
            await world.Admin.ListProductAsync(world.Furniture.Store.StoreId, productId, $"Extra {number}", 10m + number);
        }
    }

    private static string PathOf(string url) => url[url.IndexOf("/api/", StringComparison.Ordinal)..];

    private async Task<FeedView> RunAsync(FeedWorld world)
    {
        using var response = await world.Admin.PostAsync(
            $"/api/admin/stores/{world.Furniture.Store.StoreId}/feeds/stub/run", null, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<FeedView>(CancellationToken))!;
    }

    private async Task<byte[]> CollectAsync(FeedWorld world, string url)
    {
        using var response = await world.Shopper.GetAsync(PathOf(url));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsByteArrayAsync(CancellationToken);
    }

    private async Task<FeedWorld> FeedStoreAsync(bool run = true)
    {
        var furniture = await FurnitureStore.CreateAsync(_factory);
        var admin = await TestUsers.LoginAsync(_withFeed, await TestUsers.CreateAsync(_factory.Services, furniture.Store.TenantId));

        using var on = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/feeds/stub", new { IsEnabled = true }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);

        return new FeedWorld(furniture, admin, new Orders.StorefrontApi(_withFeed, furniture.Store));
    }

    private sealed record FeedWorld(FurnitureStore Furniture, HttpClient Admin, Orders.StorefrontApi Shopper);

    private sealed record FeedView(
        string Feed,
        bool IsEnabled,
        string? Url,
        DateTimeOffset? LastRunAt,
        int? LastMilliseconds,
        long? LastBytes,
        int? LastProducts,
        int? LastSkipped,
        string? LastError);
}

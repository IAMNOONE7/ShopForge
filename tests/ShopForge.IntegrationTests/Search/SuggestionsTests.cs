using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Development;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Search;

// What the search box offers while somebody types (D-227): from the second letter, the categories and products the
// words so far are probably after, from the store on the host and nothing hidden or archived, never logged, in a window
// of its own.
public sealed partial class SuggestionsTests(ToolShop shop, ShopForgeApiFactory factory) : IClassFixture<ToolShop>
{
    private const string Suggest = "/api/storefront/search/suggest";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("šr")]
    [InlineData("sr")]
    [InlineData("ŠR")]
    public async Task Two_letters_suggest_the_category_and_the_products_they_begin(string typed)
    {
        var suggested = await SuggestAsync(shop.Store, typed);

        Assert.Equal(typed, suggested.Query);
        Assert.Equal(["sroubovaky", "srouby"], suggested.Categories.Select(category => category.Slug));
        Assert.Equal(["Nářadí", "Ruční nářadí"], suggested.Categories[0].Path);
        Assert.Equal(["Spojovací materiál"], suggested.Categories[1].Path);
        Assert.Equal(6, suggested.Products.Count);
        Assert.All(suggested.Products, product => Assert.Matches(BeginsWithŠr(), product.Name));
    }

    [Fact]
    public async Task The_category_called_what_was_typed_comes_first()
    {
        var suggested = await SuggestAsync(shop.Store, "šroubováky");

        Assert.Equal("sroubovaky", suggested.Categories[0].Slug);
        Assert.Equal(
            ["TS-SK-PH2100", "TS-SP-55125", "TS-SR-1", "TS-SS-6", "TS-ST-T20100"],
            suggested.Products.Take(5).Select(product => SkuOf(product.Slug)).Order(StringComparer.Ordinal));
    }

    // What was typed may already be a whole word in another of its forms, so it is read by its forms as well as a
    // beginning: the genitive of the screwdriver set still suggests the single screwdrivers.
    [Fact]
    public async Task An_unfinished_word_is_read_by_its_forms_too() =>
        Assert.Contains("TS-SP-55125", (await SuggestAsync(shop.Store, "šroubováků")).Products.Select(product => SkuOf(product.Slug)));

    [Fact]
    public async Task A_category_is_suggested_by_its_keywords() =>
        Assert.Equal(["hrebiky"], Slugs((await SuggestAsync(shop.Store, "cvočky")).Categories));

    // A word followed by a space is finished, and the products are then exactly a search's first answers.
    [Theory]
    [InlineData("šroubováky ")]
    [InlineData("kleště ")]
    [InlineData("klíč ")]
    [InlineData("šroubovák plochý ")]
    public async Task Finished_words_suggest_what_a_search_puts_first(string typed)
    {
        var suggested = await SuggestAsync(shop.Store, typed);

        Assert.Equal(await shop.SearchAsync(typed, "&pageSize=6"), suggested.Products.Select(product => SkuOf(product.Slug)));
    }

    // A beginning of a code finds what the code begins; the same text finished is a code nothing has.
    [Fact]
    public async Task An_unfinished_code_is_a_beginning_and_a_finished_one_is_whole()
    {
        var unfinished = await SuggestAsync(shop.Store, "TS-SK-PH21");
        var finished = await SuggestAsync(shop.Store, "TS-SK-PH21 ");

        Assert.Contains("TS-SK-PH2100", unfinished.Products.Select(product => SkuOf(product.Slug)));
        Assert.Empty(finished.Products);
    }

    [Fact]
    public async Task Digits_are_the_beginning_of_a_number_until_a_space_finishes_it()
    {
        var unfinished = await SuggestAsync(shop.Store, "18", limit: 10);
        var finished = await SuggestAsync(shop.Store, "18 ", limit: 10);

        Assert.Equal(["TS-AV-18", "TS-KK-180", "TS-NC-18", "TS-NZ-18"], unfinished.Products.Select(product => SkuOf(product.Slug)).Order(StringComparer.Ordinal));
        Assert.Equal(["TS-AV-18", "TS-NC-18", "TS-NZ-18"], finished.Products.Select(product => SkuOf(product.Slug)).Order(StringComparer.Ordinal));
        Assert.Equal(("18", "18"), (unfinished.Query, finished.Query));
    }

    // The hidden sample is a screwdriver, so it would be among the first answers if it were suggested at all.
    [Fact]
    public async Task A_hidden_listing_is_never_suggested()
    {
        var screwdrivers = await SuggestAsync(shop.Store, "šroubovák", limit: 10);
        var sample = await SuggestAsync(shop.Store, "zkušební");

        Assert.DoesNotContain("TS-ZS-1", screwdrivers.Products.Select(product => SkuOf(product.Slug)));
        Assert.Empty(sample.Products);
    }

    [Fact]
    public async Task Another_stores_listings_and_an_archived_category_are_never_suggested()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var wardrobe = await furniture.Admin.CreateProductAsync("FURN-OAK-WARDROBE");
        await furniture.Admin.ListProductAsync(furniture.OtherStore.StoreId, wardrobe, "Oak Wardrobe", 900m);
        await furniture.Admin.CreateCategoryAsync(furniture.OtherStore.StoreId, "Wardrobes");
        var stools = await furniture.Admin.CreateCategoryAsync(furniture.Store.StoreId, "Stools");
        using var archived = await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/categories/{stools}/archive", null, CancellationToken);

        var here = await SuggestAsync(furniture.Store, "wardrobe");
        var there = await SuggestAsync(furniture.OtherStore, "wardrobe");
        var stool = await SuggestAsync(furniture.Store, "stool");

        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        Assert.Equal((0, 0), (here.Categories.Count, here.Products.Count));
        Assert.Equal(["wardrobes"], Slugs(there.Categories));
        Assert.Equal(["Oak Wardrobe"], Names(there.Products));
        Assert.Empty(stool.Categories);
        Assert.Equal(["Beech Stool"], Names(stool.Products));
    }

    // English drops "be" as a stop word, and somebody typing "bench" is offered it from the second letter. The same two
    // letters finished are the word English drops.
    [Fact]
    public async Task A_beginning_its_language_would_drop_is_still_suggested()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var unfinished = await SuggestAsync(furniture.Store, "be");
        var finished = await SuggestAsync(furniture.Store, "be ");

        Assert.Equal(["Beech Stool", "Oak Bench"], Names(unfinished.Products).Order(StringComparer.Ordinal));
        Assert.Empty(finished.Products);
    }

    [Fact]
    public async Task A_category_and_a_product_of_one_name_are_each_suggested()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await furniture.Admin.CreateCategoryAsync(furniture.Store.StoreId, "Oak Bench");

        var suggested = await SuggestAsync(furniture.Store, "oak bench");

        Assert.Equal(["oak-bench"], Slugs(suggested.Categories));
        Assert.Equal(["Oak Bench"], Names(suggested.Products));
    }

    [Theory]
    [InlineData(null, 6)]
    [InlineData(3, 3)]
    [InlineData(10, 10)]
    [InlineData(50, 10)]
    [InlineData(0, 1)]
    public async Task Six_products_unless_asked_for_up_to_ten(int? limit, int count) =>
        Assert.Equal(count, (await SuggestAsync(shop.Store, "šr", limit)).Products.Count);

    [Theory]
    [InlineData("")]
    [InlineData("š")]
    [InlineData("   ")]
    [InlineData(" š ")]
    public async Task Under_two_letters_nothing_is_suggested(string typed)
    {
        var suggested = await SuggestAsync(shop.Store, typed);

        Assert.Equal((typed.Trim(), 0, 0), (suggested.Query, suggested.Categories.Count, suggested.Products.Count));
    }

    [Fact]
    public async Task Two_hundred_characters_are_read_and_no_more()
    {
        var suggested = await SuggestAsync(shop.Store, string.Concat(Enumerable.Repeat("šroubovák ", 30)));

        Assert.Equal(string.Concat(Enumerable.Repeat("šroubovák ", 20)).TrimEnd(), suggested.Query);
        Assert.NotEmpty(suggested.Products);
    }

    // The category list is one query of its own, so one letter, which answers before asking anything, costs one fewer;
    // two letters cost the store's settings, its categories and its products.
    [Fact]
    public async Task A_suggestion_costs_three_round_trips_and_one_letter_none()
    {
        var categories = await QueriesAsync(shop.Store, "/api/storefront/categories");
        var oneLetter = await QueriesAsync(shop.Store, $"{Suggest}?q=š");
        var twoLetters = await QueriesAsync(shop.Store, $"{Suggest}?q=šr");

        Assert.Equal(categories - 1, oneLetter);
        Assert.Equal(oneLetter + 3, twoLetters);
    }

    [Fact]
    public async Task Nothing_suggested_is_logged()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        await SuggestAsync(furniture.Store, "walnut");
        var afterSuggesting = await LoggedAsync(furniture);

        using var shopper = new StorefrontApi(factory, furniture.Store);
        await shopper.GetStringAsync("/api/storefront/products?q=walnut");

        Assert.Equal((0, 1), (afterSuggesting, await LoggedAsync(furniture)));
    }

    [Fact]
    public async Task A_suggestion_is_kept_briefly_and_by_the_browser_alone()
    {
        using var shopper = new StorefrontApi(factory, shop.Store);
        using var response = await shopper.GetAsync($"{Suggest}?q=šr");

        Assert.Equal((true, TimeSpan.FromSeconds(60), false), (response.Headers.CacheControl!.Private, response.Headers.CacheControl.MaxAge, response.Headers.CacheControl.Public));
    }

    // Somebody typing has a window of their own: a spent storefront window leaves suggestions alone, and a burst of
    // suggestions is refused without spending it.
    [Fact]
    public async Task Typing_has_a_window_of_its_own()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var capped = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Writes:PermitLimit", "1");
            builder.UseSetting("RateLimiting:Typing:PermitLimit", "2");
        });
        using var shopper = new StorefrontApi(capped, furniture.Store);
        var statuses = new List<HttpStatusCode>();

        foreach (var path in new[] { "/api/storefront/products", "/api/storefront/products", $"{Suggest}?q=oak", $"{Suggest}?q=oak", $"{Suggest}?q=oak" })
        {
            using var response = await shopper.GetAsync(path);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests],
            statuses);
    }

    private async Task<Suggestions> SuggestAsync(TestStore store, string typed, int? limit = null)
    {
        using var shopper = new StorefrontApi(factory, store);

        return await shopper.GetJsonAsync<Suggestions>($"{Suggest}?q={Uri.EscapeDataString(typed)}{(limit is { } most ? $"&limit={most}" : "")}");
    }

    private async Task<int> QueriesAsync(TestStore store, string path)
    {
        using var shopper = new StorefrontApi(factory, store);

        // One run to warm the plans, as the load baseline does, then the one that is counted.
        using var warmed = await shopper.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, warmed.StatusCode);

        var tally = QueryCounter.NewTally();
        using var counted = await shopper.GetAsync(path, (QueryCounter.HeaderName, tally));
        Assert.Equal(HttpStatusCode.OK, counted.StatusCode);

        return factory.Queries[tally];
    }

    private async Task<int> LoggedAsync(FurnitureStore furniture) =>
        await factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM catalog.search_queries WHERE store_id = {furniture.Store.StoreId}")
            .SingleAsync(CancellationToken));

    private static string SkuOf(string slug) => ToolShopCatalogue.Products.Single(product => product.Slug == slug).Sku;

    private static List<string> Slugs(List<SuggestedCategory> categories) => [.. categories.Select(category => category.Slug)];

    private static List<string> Names(List<SuggestedProduct> products) => [.. products.Select(product => product.Name)];

    [GeneratedRegex(@"(^|\s)šr", RegexOptions.IgnoreCase)]
    private static partial Regex BeginsWithŠr();

    private sealed record Suggestions(string Query, List<SuggestedCategory> Categories, List<SuggestedProduct> Products);

    private sealed record SuggestedCategory(string Name, string Slug, List<string> Path);

    private sealed record SuggestedProduct(Guid Id, string Slug, string Name, decimal Price, string? ImageUrl);
}

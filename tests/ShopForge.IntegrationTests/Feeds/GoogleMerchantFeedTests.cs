using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Feeds;

// The shape Merchant Center is published to expect. Nothing has ever been submitted to it, so this pins what
// ShopForge writes and the file it comes from is the one to check against the current specification (D-169).
public sealed class GoogleMerchantFeedTests(ShopForgeApiFactory factory)
{
    private static readonly XNamespace G = "http://base.google.com/ns/1.0";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_document_is_an_rss_channel_of_items_in_googles_namespace()
    {
        var furniture = await OnAsync();

        var feed = await RunAsync(furniture);

        Assert.Equal("rss", feed.Name.LocalName);
        Assert.Equal("2.0", feed.Attribute("version")?.Value);
        Assert.Equal(G.NamespaceName, feed.GetNamespaceOfPrefix("g")?.NamespaceName);
        Assert.Equal(furniture.Store.Name, feed.Element("channel")!.Element("title")!.Value);
        Assert.NotEmpty(Items(feed));
    }

    [Fact]
    public async Task One_thing_for_sale_carries_everything_google_asks_for()
    {
        var furniture = await OnAsync();
        await BrandAsync(furniture, "oak-chair", "Thonet");

        var item = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        Assert.Equal("Oak Chair", item.Element("title")!.Value);
        Assert.Equal($"https://{furniture.Store.HostName}/p/oak-chair", item.Element("link")!.Value);
        Assert.Equal("100.00 EUR", item.Element(G + "price")!.Value);
        Assert.Equal("in_stock", item.Element(G + "availability")!.Value);
        Assert.Equal("Thonet", item.Element(G + "brand")!.Value);
        Assert.Equal("8594000000006", item.Element(G + "gtin")!.Value);
        Assert.Equal("new", item.Element(G + "condition")!.Value);
    }

    // A shop with none left says so rather than going quiet, because an absent availability is not "none".
    [Fact]
    public async Task A_thing_with_none_left_says_so()
    {
        var furniture = await OnAsync();
        await furniture.Admin.StockAsync(furniture.ProductIds["beech-stool"], 0);

        var item = Item(await RunAsync(furniture), FurnitureStore.SkuOf("beech-stool"));

        Assert.Equal("out_of_stock", item.Element(G + "availability")!.Value);
    }

    // One product in several sizes is one thing, and the group ties them together.
    [Fact]
    public async Task Several_forms_of_one_product_are_tied_together_and_one_form_is_not()
    {
        var furniture = await OnAsync();
        var shirts = await ShirtStore.CreateAsync(factory);
        var onItsOwn = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        var feed = await RunAsync(shirts.Furniture.Store, shirts.Furniture.Admin);
        var grouped = Items(feed).Where(item => item.Element(G + "item_group_id") is not null).ToList();

        Assert.Null(onItsOwn.Element(G + "item_group_id"));
        Assert.NotEmpty(grouped);
        Assert.Single(grouped.Select(item => item.Element(G + "item_group_id")!.Value).Distinct());
    }

    // A thing nobody can match to anybody else's listing is declared as such rather than left ambiguous.
    [Fact]
    public async Task A_thing_with_no_barcode_and_no_part_number_says_it_has_no_identifier()
    {
        var furniture = await OnAsync();
        await ForgetTheBarcodeAsync(furniture, "oak-bench");

        var item = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-bench"));

        Assert.Null(item.Element(G + "gtin"));
        Assert.Equal("no", item.Element(G + "identifier_exists")!.Value);
    }

    // A shop can only publish the delivery it has actually described. A method that has not said which
    // countries it serves produces nothing rather than a promise the shop never made.
    [Fact]
    public async Task Shipping_is_stated_only_where_the_shop_has_said_which_countries_it_serves()
    {
        var furniture = await OnAsync();
        var before = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        using var served = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/courier",
            new { Name = "Courier", Price = 4.90m, VatRate = 21m, IsActive = true, RequiresPickupPoint = false, MaxWeightGrams = (int?)null, Countries = new[] { "IE", "CZ" } },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        var after = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));
        var shipping = after.Elements(G + "shipping").ToList();

        Assert.Empty(before.Elements(G + "shipping"));
        Assert.Equal(2, shipping.Count);
        Assert.Equal(["CZ", "IE"], shipping.Select(rate => rate.Element(G + "country")!.Value).Order());
        Assert.All(shipping, rate => Assert.Equal("4.90 EUR", rate.Element(G + "price")!.Value));
    }

    // Told before Google tells them: a merchant who finds out three days later has lost three days.
    [Fact]
    public async Task The_check_lists_exactly_what_google_would_refuse_and_why()
    {
        var furniture = await OnAsync();

        var before = await CheckAsync(furniture);
        await BrandAsync(furniture, "oak-chair", "Thonet");
        var after = await CheckAsync(furniture);

        var chairBefore = before.Single(problem => problem.Sku == FurnitureStore.SkuOf("oak-chair"));
        var chairAfter = after.Single(problem => problem.Sku == FurnitureStore.SkuOf("oak-chair"));

        // None of these fixtures has a picture, so that one is said about every product and is the proof the
        // check reads the catalogue rather than a list of names.
        Assert.Contains(chairBefore.Problems, problem => problem.Contains("no brand", StringComparison.Ordinal));
        Assert.Contains(chairBefore.Problems, problem => problem.Contains("no image", StringComparison.Ordinal));

        // Naming the brand answers that one and leaves the others alone.
        Assert.DoesNotContain(chairAfter.Problems, problem => problem.Contains("no brand", StringComparison.Ordinal));
        Assert.Contains(chairAfter.Problems, problem => problem.Contains("no image", StringComparison.Ordinal));
    }

    private async Task<List<ProblemView>> CheckAsync(FurnitureStore furniture) =>
        (await furniture.Admin.GetFromJsonAsync<List<ProblemView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/feeds/google/check", CancellationToken))!;

    private static List<XElement> Items(XElement feed) => [.. feed.Element("channel")!.Elements("item")];

    private static XElement Item(XElement feed, string sku) =>
        Items(feed).Single(item => item.Element(G + "id")!.Value == sku);

    private async Task<FurnitureStore> OnAsync()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var on = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/feeds/google", new { IsEnabled = true }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);

        return furniture;
    }

    private Task<XElement> RunAsync(FurnitureStore furniture) => RunAsync(furniture.Store, furniture.Admin);

    private async Task<XElement> RunAsync(TestStore store, HttpClient admin)
    {
        using var on = await admin.PutAsJsonAsync(
            $"/api/admin/stores/{store.StoreId}/feeds/google", new { IsEnabled = true }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);

        using var ran = await admin.PostAsync($"/api/admin/stores/{store.StoreId}/feeds/google/run", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ran.StatusCode);
        var produced = await ran.Content.ReadFromJsonAsync<FeedView>(CancellationToken);

        using var shopper = new Orders.StorefrontApi(factory, store);
        var url = produced!.Url!;

        return XDocument.Parse(await shopper.GetStringAsync(url[url.IndexOf("/api/", StringComparison.Ordinal)..])).Root!;
    }

    // The endpoint replaces what a product is, so everything it already had goes back with the brand.
    private async Task BrandAsync(FurnitureStore furniture, string slug, string brand)
    {
        using var branded = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{furniture.ProductIds[slug]}",
            new { Ean = "8594000000006", WeightGrams = 850, Brand = brand },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, branded.StatusCode);
    }

    private async Task ForgetTheBarcodeAsync(FurnitureStore furniture, string slug)
    {
        using var forgotten = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{furniture.ProductIds[slug]}",
            new { Ean = (string?)null, WeightGrams = (int?)null },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, forgotten.StatusCode);
    }

    private sealed record FeedView(string Feed, bool IsEnabled, string? Url);

    private sealed record ProblemView(string Sku, string Name, List<string> Problems);
}

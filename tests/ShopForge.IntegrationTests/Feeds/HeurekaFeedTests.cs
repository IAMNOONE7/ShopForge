using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Feeds;

// Heureka's SHOPITEM document. Nothing has ever been submitted to Heureka, so this pins what ShopForge writes
// and names the one file to check against the specification (D-170).
public sealed class HeurekaFeedTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_document_is_a_shop_of_shopitems()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var feed = await RunAsync(furniture);

        Assert.Equal("SHOP", feed.Name.LocalName);
        Assert.NotEmpty(feed.Elements("SHOPITEM"));
        Assert.All(feed.Elements("SHOPITEM"), item => Assert.NotNull(item.Element("ITEM_ID")));
    }

    [Fact]
    public async Task One_thing_for_sale_carries_what_heureka_asks_for()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var item = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        Assert.Equal("Oak Chair", item.Element("PRODUCTNAME")!.Value);
        Assert.Equal("Oak Chair", item.Element("PRODUCT")!.Value);
        Assert.Equal($"https://{furniture.Store.HostName}/p/oak-chair", item.Element("URL")!.Value);
        Assert.Equal("100.00", item.Element("PRICE_VAT")!.Value);
        Assert.Equal("8594000000006", item.Element("EAN")!.Value);
        Assert.Equal("0", item.Element("DELIVERY_DATE")!.Value);
    }

    // The engine's taxonomy is not the merchant's, and nobody can map between them by machine. Until somebody
    // has said, the field is left out rather than filled with the shop's own words.
    [Fact]
    public async Task A_category_says_nothing_until_the_merchant_maps_it_and_then_says_what_they_said()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var before = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        await MapAsync(furniture, "Nábytek | Židle | Jídelní židle");
        var after = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        Assert.Null(before.Element("CATEGORYTEXT"));
        Assert.Equal("Nábytek | Židle | Jídelní židle", after.Element("CATEGORYTEXT")!.Value);
    }

    // One table for every engine, so mapping for Heureka does not map for Google and the other way round.
    [Fact]
    public async Task A_mapping_belongs_to_the_engine_it_was_made_for()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await MapAsync(furniture, "Nábytek | Židle");

        var heureka = await MappingsAsync(furniture, "heureka");
        var google = await MappingsAsync(furniture, "google");

        Assert.Equal("Nábytek | Židle", heureka.Single(category => category.Name == "Chairs").EngineCategory);
        Assert.Null(google.Single(category => category.Name == "Chairs").EngineCategory);
    }

    // And the document proves it, not just the list: a category named for one engine is not published to
    // another, which is the point of keeping the engine in the key rather than in the table's name (D-147).
    [Fact]
    public async Task A_category_named_for_another_engine_is_not_published_here()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await MapAsync(furniture, "Furniture > Chairs", feed: "google");

        var item = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        Assert.Null(item.Element("CATEGORYTEXT"));
    }

    // The work still to do, in one list: every category, with what it has been called or nothing.
    [Fact]
    public async Task The_mapping_list_names_what_is_still_unmapped()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var categories = await MappingsAsync(furniture, "heureka");

        Assert.Contains(categories, category => category.Name == "Chairs" && category.EngineCategory is null);
    }

    [Fact]
    public async Task A_mapping_can_be_taken_back()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await MapAsync(furniture, "Nábytek | Židle");

        await MapAsync(furniture, null);
        var categories = await MappingsAsync(furniture, "heureka");

        Assert.Null(categories.Single(category => category.Name == "Chairs").EngineCategory);
    }

    // What a shop chooses to publish, and only that: a measurement can be in a comparison table without being
    // on the shop's own page, and the other way round.
    [Fact]
    public async Task Parameters_come_from_the_attributes_a_shop_publishes()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var before = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        await PublishAttributeAsync(furniture, "Material");
        await PublishAttributeAsync(furniture, "Width");
        var after = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));
        var parameters = after.Elements("PARAM").ToList();

        Assert.Empty(before.Elements("PARAM"));
        Assert.Equal(2, parameters.Count);
        Assert.Equal("Oak", parameters.Single(p => p.Element("PARAM_NAME")!.Value == "Material").Element("VAL")!.Value);

        var width = parameters.Single(p => p.Element("PARAM_NAME")!.Value == "Width");
        Assert.Equal("45", width.Element("VAL")!.Value);
        Assert.Equal("cm", width.Element("UNIT")!.Value);
    }

    [Fact]
    public async Task Several_forms_of_one_product_share_an_item_group()
    {
        var shirts = await ShirtStore.CreateAsync(factory);

        var feed = await RunAsync(shirts.Furniture);
        var grouped = feed.Elements("SHOPITEM").Where(item => item.Element("ITEMGROUP_ID") is not null).ToList();

        Assert.NotEmpty(grouped);
        Assert.Single(grouped.Select(item => item.Element("ITEMGROUP_ID")!.Value).Distinct());
    }

    private static XElement Item(XElement feed, string sku) =>
        feed.Elements("SHOPITEM").Single(item => item.Element("ITEM_ID")!.Value == sku);

    private async Task MapAsync(FurnitureStore furniture, string? engineCategory, string feed = "heureka")
    {
        using var mapped = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/feeds/{feed}/categories/{furniture.ChairsCategoryId}",
            new { EngineCategory = engineCategory },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, mapped.StatusCode);
    }

    private async Task<List<MappedView>> MappingsAsync(FurnitureStore furniture, string feed) =>
        (await furniture.Admin.GetFromJsonAsync<List<MappedView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/feeds/{feed}/categories", CancellationToken))!;

    private async Task PublishAttributeAsync(FurnitureStore furniture, string name)
    {
        var attributes = await furniture.Admin.GetFromJsonAsync<List<AttributeView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes", CancellationToken);
        var attribute = attributes!.Single(candidate => candidate.Name == name);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{attribute.Id}",
            new
            {
                attribute.Name,
                attribute.Unit,
                attribute.IsFilterable,
                attribute.IsVisibleOnProductPage,
                attribute.SortOrder,
                IsInFeeds = true,
            },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task<XElement> RunAsync(FurnitureStore furniture)
    {
        using var on = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/feeds/heureka", new { IsEnabled = true }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);

        using var ran = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/feeds/heureka/run", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ran.StatusCode);
        var produced = await ran.Content.ReadFromJsonAsync<FeedView>(CancellationToken);

        using var shopper = new Orders.StorefrontApi(factory, furniture.Store);
        var url = produced!.Url!;

        return XDocument.Parse(await shopper.GetStringAsync(url[url.IndexOf("/api/", StringComparison.Ordinal)..])).Root!;
    }

    private sealed record FeedView(string Feed, bool IsEnabled, string? Url);

    private sealed record MappedView(Guid CategoryId, string Name, string? EngineCategory);

    private sealed record AttributeView(Guid Id, string Name, string? Unit, bool IsFilterable, bool IsVisibleOnProductPage, int SortOrder);
}

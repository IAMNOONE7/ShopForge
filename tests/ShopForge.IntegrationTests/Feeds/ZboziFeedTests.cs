using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Feeds;

// Zboží.cz's SHOPITEM document. As with Heureka's, nothing has ever been submitted to Seznam, so this pins
// what ShopForge writes and names the one file to check against the specification (D-171).
public sealed class ZboziFeedTests(ShopForgeApiFactory factory)
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
    public async Task One_thing_for_sale_carries_what_zbozi_asks_for()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var item = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        Assert.Equal("Oak Chair", item.Element("PRODUCTNAME")!.Value);
        Assert.Equal($"https://{furniture.Store.HostName}/p/oak-chair", item.Element("URL")!.Value);
        Assert.Equal("100.00", item.Element("PRICE_VAT")!.Value);
        Assert.Equal("8594000000006", item.Element("EAN")!.Value);
        Assert.Equal("0", item.Element("DELIVERY_DATE")!.Value);
    }

    // The maker and the maker's own number for the thing, under the names Seznam reads.
    [Fact]
    public async Task The_maker_appears_once_the_merchant_has_named_one()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var before = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        await DescribeAsync(furniture, "oak-chair", brand: "Thonet", partNumber: "214-OAK");
        var after = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        Assert.Null(before.Element("MANUFACTURER"));
        Assert.Null(before.Element("PRODUCTNO"));
        Assert.Equal("Thonet", after.Element("MANUFACTURER")!.Value);
        Assert.Equal("214-OAK", after.Element("PRODUCTNO")!.Value);
    }

    [Fact]
    public async Task A_photograph_is_published_at_the_shops_own_address()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var before = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        using var uploaded = await furniture.Admin.UploadImageAsync(furniture.ProductIds["oak-chair"], AdminCatalogApi.PngBytes);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var after = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        Assert.Null(before.Element("IMGURL"));
        Assert.StartsWith($"https://{furniture.Store.HostName}/", after.Element("IMGURL")!.Value, StringComparison.Ordinal);
    }

    // DELIVERY_DATE is days until dispatch, so a shop with none left says nothing rather than promising
    // today. The thing itself stays in the document: an engine is told the availability (D-168).
    [Fact]
    public async Task Nothing_left_in_stock_promises_no_date()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await furniture.Admin.StockAsync(furniture.ProductIds["oak-chair"], 0);

        var item = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        Assert.Null(item.Element("DELIVERY_DATE"));
        Assert.Equal("Oak Chair", item.Element("PRODUCTNAME")!.Value);
    }

    // Seznam's taxonomy is its own, and nobody can map onto it by machine. Until somebody has said, the field
    // is left out rather than filled with the shop's own words.
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

    // The two engines share a table and nothing else: what was said for Heureka is not published to Seznam,
    // even though both engines call the element CATEGORYTEXT and both would accept the string (D-170).
    [Fact]
    public async Task A_category_named_for_heureka_is_not_published_here()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await MapAsync(furniture, "Nábytek | Židle", feed: "heureka");

        var item = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));
        var mappings = await MappingsAsync(furniture, "zbozi");

        Assert.Null(item.Element("CATEGORYTEXT"));
        Assert.Null(mappings.Single(category => category.Name == "Chairs").EngineCategory);
    }

    // Zboží's element set is not Heureka's, and this is the test that fails if the two composers are ever
    // merged: the attributes a shop publishes become Heureka PARAMs and are not part of this document.
    [Fact]
    public async Task The_document_carries_only_the_elements_zbozi_reads()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await PublishAttributeAsync(furniture, "Material");

        var item = Item(await RunAsync(furniture), FurnitureStore.SkuOf("oak-chair"));

        Assert.Empty(item.Elements("PARAM"));
        Assert.Null(item.Element("PRODUCT"));
    }

    [Fact]
    public async Task Several_forms_of_one_product_are_separate_items_here()
    {
        var shirts = await ShirtStore.CreateAsync(factory);

        var feed = await RunAsync(shirts.Furniture);

        Assert.True(feed.Elements("SHOPITEM").Count() > 1);
        Assert.All(feed.Elements("SHOPITEM"), item => Assert.Null(item.Element("ITEMGROUP_ID")));
    }

    private static XElement Item(XElement feed, string sku) =>
        feed.Elements("SHOPITEM").Single(item => item.Element("ITEM_ID")!.Value == sku);

    private async Task MapAsync(FurnitureStore furniture, string? engineCategory, string feed = "zbozi")
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

    // The endpoint replaces what a product is, so everything it already had goes back with the new facts.
    private async Task DescribeAsync(FurnitureStore furniture, string slug, string brand, string partNumber)
    {
        using var described = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{furniture.ProductIds[slug]}",
            new { Ean = "8594000000006", WeightGrams = 850, Brand = brand, PartNumber = partNumber },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, described.StatusCode);
    }

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
            $"/api/admin/stores/{furniture.Store.StoreId}/feeds/zbozi", new { IsEnabled = true }, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);

        using var ran = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/feeds/zbozi/run", null, CancellationToken);
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

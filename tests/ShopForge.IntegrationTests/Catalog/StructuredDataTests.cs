using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Catalog;

// The documents a page hands over with its data, composed from what the page already knows (D-173). Nothing
// renders them yet — that is 28m — so this is the whole of the contract.
public sealed class StructuredDataTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_product_page_declares_the_thing_for_sale()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var product = Document(await PageAsync(furniture, "/api/storefront/products/oak-chair"), "Product");

        Assert.Equal("Oak Chair", (string?)product["name"]);
        Assert.Equal(FurnitureStore.SkuOf("oak-chair"), (string?)product["sku"]);
        Assert.Equal($"https://{furniture.Store.HostName}/p/oak-chair", (string?)product["url"]);
        Assert.Equal("100", (string?)product["offers"]!["price"]);
        Assert.Equal("EUR", (string?)product["offers"]!["priceCurrency"]);
        Assert.Equal("https://schema.org/InStock", (string?)product["offers"]!["availability"]);
    }

    // The availability is the warehouse's answer, taken at the moment the page is built.
    [Fact]
    public async Task Availability_follows_the_stock_the_shop_actually_has()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await furniture.Admin.StockAsync(furniture.ProductIds["oak-chair"], 0);

        var product = Document(await PageAsync(furniture, "/api/storefront/products/oak-chair"), "Product");

        Assert.Equal("https://schema.org/OutOfStock", (string?)product["offers"]!["availability"]);
    }

    [Fact]
    public async Task A_product_nobody_has_reviewed_declares_no_rating()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var product = Document(await PageAsync(furniture, "/api/storefront/products/oak-chair"), "Product");

        Assert.False(product.AsObject().ContainsKey("aggregateRating"));
    }

    // A barcode names one particular thing, so a listing sold in several forms does not claim one form's.
    [Fact]
    public async Task A_listing_sold_in_several_forms_declares_an_aggregate_and_no_barcode()
    {
        var shirts = await ShirtStore.CreateAsync(factory);

        var product = Document(await PageAsync(shirts.Furniture, $"/api/storefront/products/{shirts.Slug}"), "Product");

        Assert.Equal("AggregateOffer", (string?)product["offers"]!["@type"]);
        Assert.Equal(2, (int?)product["offers"]!["offerCount"]);
        Assert.False(product.AsObject().ContainsKey("gtin13"));
    }

    [Fact]
    public async Task A_single_form_carries_the_barcode_the_merchant_entered()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var described = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{furniture.ProductIds["oak-chair"]}",
            new { Ean = "8594000000006", WeightGrams = 850, Brand = "Thonet" },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, described.StatusCode);

        var product = Document(await PageAsync(furniture, "/api/storefront/products/oak-chair"), "Product");

        Assert.Equal("8594000000006", (string?)product["gtin13"]);
        Assert.Equal("Thonet", (string?)product["brand"]!["name"]);
    }

    // The trail a crawler walks back up, from the category tree 28f built.
    [Fact]
    public async Task A_product_page_declares_the_trail_down_to_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var top = await furniture.Admin.CreateCategoryAsync(furniture.Store.StoreId, "Furniture");
        await MoveUnderAsync(furniture, furniture.ChairsCategoryId, top);

        var crumbs = Document(await PageAsync(furniture, "/api/storefront/products/oak-chair"), "BreadcrumbList");
        var steps = crumbs["itemListElement"]!.AsArray();

        Assert.Equal([1, 2, 3, 4], steps.Select(step => (int?)step!["position"]));
        Assert.Equal(["Furniture", "Chairs", "Oak Chair"], steps.Skip(1).Select(step => (string?)step!["name"]));
        Assert.Equal($"https://{furniture.Store.HostName}/", (string?)steps[0]!["item"]);
        Assert.Null(steps[3]!["item"]);
    }

    [Fact]
    public async Task A_category_page_declares_its_own_trail_and_nothing_else()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var page = await PageAsync(furniture, "/api/storefront/products?category=chairs");
        var crumbs = Document(page, "BreadcrumbList");

        Assert.Equal(["Chairs"], crumbs["itemListElement"]!.AsArray().Skip(1).Select(step => (string?)step!["name"]));
        Assert.Null(Find(page, "Organization"));
    }

    // The same list with nothing chosen is the shop's front page, which is where it says who it is.
    [Fact]
    public async Task The_front_page_declares_who_the_shop_is()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var page = await PageAsync(furniture, "/api/storefront/products");
        var organization = Document(page, "Organization");
        var site = Document(page, "WebSite");

        Assert.Equal($"https://{furniture.Store.HostName}/", (string?)organization["url"]);
        Assert.Equal("PostalAddress", (string?)organization["address"]!["@type"]);
        Assert.Equal($"https://{furniture.Store.HostName}/", (string?)site["url"]);
        Assert.Null(Find(page, "BreadcrumbList"));
    }

    private async Task<JsonNode> PageAsync(FurnitureStore furniture, string path)
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);

        return JsonNode.Parse(await shopper.GetStringAsync(path))!;
    }

    private static JsonNode Document(JsonNode page, string type) =>
        Find(page, type) ?? throw new InvalidOperationException($"No {type} document: {page["jsonLd"]!.ToJsonString()}");

    private static JsonNode? Find(JsonNode page, string type) =>
        page["jsonLd"]!.AsArray().SingleOrDefault(document => (string?)document!["@type"] == type);

    private static async Task MoveUnderAsync(FurnitureStore furniture, Guid categoryId, Guid parentId)
    {
        var categories = await furniture.Admin.GetFromJsonAsync<List<MovedCategory>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories", CancellationToken);
        var category = categories!.Single(candidate => candidate.Id == categoryId);

        using var moved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories/{categoryId}",
            new { category.Name, category.Slug, category.SortOrder, ParentId = parentId },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
    }

    private sealed record MovedCategory(Guid Id, string Name, string Slug, int SortOrder);
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ShopForge.IntegrationTests.Catalog;

public sealed class AttributeTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Product_page_shows_visible_attributes_as_typed_values()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var detail = await GetAsync<ProductDetail>(furniture.Store, "/api/storefront/products/oak-chair");
        var attributes = detail.Attributes.ToDictionary(attribute => attribute.Code, attribute => attribute.Value.ToString());

        Assert.Equal("Oak", attributes["material"]);
        Assert.Equal(45m, detail.Attributes.Single(attribute => attribute.Code == "width").Value.GetDecimal());
        Assert.Equal("False", attributes["foldable"], ignoreCase: true);
        Assert.Equal("""["Natural"]""", attributes["colors"]);
        Assert.Equal("2024-03-01", attributes["released"]);
        Assert.Equal("Oil once a year", attributes["care"]);
        Assert.False(attributes.ContainsKey("seats"));
        Assert.Equal("cm", detail.Attributes.Single(attribute => attribute.Code == "width").Unit);
    }

    [Theory]
    [InlineData("""{ "seats": "two" }""", "values.seats")]
    [InlineData("""{ "seats": 1.5 }""", "values.seats")]
    [InlineData("""{ "material": "plastic" }""", "values.material")]
    [InlineData("""{ "colors": ["black", "black"] }""", "values.colors")]
    [InlineData("""{ "released": "01.03.2024" }""", "values.released")]
    [InlineData("""{ "foldable": "yes" }""", "values.foldable")]
    [InlineData("""{ "height": 10 }""", "values.height")]
    public async Task Invalid_attribute_values_are_rejected(string values, string field)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var response = await furniture.Admin.SetAttributesAsync(
            furniture.Store.StoreId, furniture.Products["oak-chair"], JsonSerializer.Deserialize<JsonElement>(values));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Text_attributes_cannot_be_filters()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var response = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes",
            new { Name = "Notes", Type = "text", IsFilterable = true },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Values_can_be_replaced_repeatedly()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chair = furniture.Products["walnut-chair"];

        using var first = await furniture.Admin.SetAttributesAsync(furniture.Store.StoreId, chair, new { material = "oak", colors = new[] { "white", "black" } });
        using var second = await furniture.Admin.SetAttributesAsync(furniture.Store.StoreId, chair, new { material = "oak", colors = new[] { "black" }, seats = 2 });
        var stored = await furniture.Admin.GetFromJsonAsync<ProductValues>(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{chair}/attributes", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(["colors", "material", "seats"], stored!.Values.Keys.Order());
        Assert.Equal("""["black"]""", stored.Values["colors"].ToString());
    }

    [Theory]
    [InlineData("f.material=oak", "oak-bench,oak-chair")]
    [InlineData("f.material=oak,walnut", "oak-bench,oak-chair,walnut-chair")]
    [InlineData("f.colors=black,white", "beech-stool,walnut-chair")]
    [InlineData("f.width=40..60", "oak-chair,walnut-chair")]
    [InlineData("f.width=..40", "beech-stool")]
    [InlineData("f.width=100..", "oak-bench")]
    [InlineData("f.foldable=true", "walnut-chair")]
    [InlineData("f.seats=2..", "oak-bench")]
    [InlineData("f.released=2024-01-01..2024-12-31", "oak-chair")]
    [InlineData("f.material=oak&f.width=..60", "oak-chair")]
    [InlineData("category=chairs&f.material=oak", "oak-chair")]
    public async Task Filters_narrow_the_product_list(string query, string expectedSlugs)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var page = await GetAsync<ProductPage>(furniture.Store, $"/api/storefront/products?{query}");

        Assert.Equal(expectedSlugs, string.Join(',', page.Items.Select(item => item.Slug).Order()));
        Assert.Equal(page.Items.Count, page.TotalCount);
    }

    [Fact]
    public async Task Facets_count_alternatives_while_their_own_filter_is_selected()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var page = await GetAsync<ProductPage>(furniture.Store, "/api/storefront/products?f.material=oak&f.foldable=false");
        var material = page.Filters.Single(filter => filter.Code == "material");
        var width = page.Filters.Single(filter => filter.Code == "width");
        var foldable = page.Filters.Single(filter => filter.Code == "foldable");

        Assert.Equal(["oak:2:True", "walnut:0:False", "beech:1:False"], material.Options!.Select(option => $"{option.Code}:{option.Count}:{option.Selected}"));
        Assert.Equal((45m, 120m), (width.Min!.Value.GetDecimal(), width.Max!.Value.GetDecimal()));
        Assert.Equal((0, 2), (foldable.TrueCount, foldable.FalseCount));
    }

    [Fact]
    public async Task Category_pages_offer_only_the_categorys_filters()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var all = await GetAsync<ProductPage>(furniture.Store, "/api/storefront/products");
        var chairs = await GetAsync<ProductPage>(furniture.Store, "/api/storefront/products?category=chairs");

        Assert.Equal(["colors", "foldable", "material", "released", "seats", "width"], all.Filters.Select(filter => filter.Code));
        Assert.Equal(["material", "width"], chairs.Filters.Select(filter => filter.Code));
    }

    [Theory]
    [InlineData("price", "beech-stool,oak-chair,walnut-chair,oak-bench")]
    [InlineData("-price", "oak-bench,walnut-chair,oak-chair,beech-stool")]
    [InlineData("attr.width", "beech-stool,oak-chair,walnut-chair,oak-bench")]
    [InlineData("-attr.released", "walnut-chair,oak-chair,beech-stool,oak-bench")]
    [InlineData("attr.released", "beech-stool,oak-chair,walnut-chair,oak-bench")]
    public async Task Products_can_be_sorted_by_price_and_attributes(string sort, string expectedSlugs)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var page = await GetAsync<ProductPage>(furniture.Store, $"/api/storefront/products?sort={sort}");

        Assert.Equal(expectedSlugs, string.Join(',', page.Items.Select(item => item.Slug)));
    }

    [Theory]
    [InlineData("f.care=oil")]
    [InlineData("f.unknown=1")]
    [InlineData("f.width=abc")]
    [InlineData("f.width=1..2..3")]
    [InlineData("f.material=plastic")]
    [InlineData("sort=attr.material")]
    [InlineData("sort=popularity")]
    public async Task Invalid_filters_and_sorts_are_rejected(string query)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var response = await factory.CreateClient().GetAsync(
            $"http://{furniture.Store.HostName}/api/storefront/products?{query}", CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Attributes_of_one_store_are_unknown_to_another()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var otherPage = await GetAsync<ProductPage>(furniture.OtherStore, "/api/storefront/products");
        using var filterByForeignAttribute = await factory.CreateClient().GetAsync(
            $"http://{furniture.OtherStore.HostName}/api/storefront/products?f.material=oak", CancellationToken);
        using var setForeignAttribute = await furniture.Admin.SetAttributesAsync(
            furniture.OtherStore.StoreId,
            await furniture.Admin.ListProductAsync(furniture.OtherStore.StoreId, await furniture.Admin.CreateProductAsync(), "Lamp", 10m),
            new { material = "oak" });

        Assert.Empty(otherPage.Filters);
        Assert.Equal(HttpStatusCode.BadRequest, filterByForeignAttribute.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, setForeignAttribute.StatusCode);
    }

    private async Task<T> GetAsync<T>(TestStore store, string pathAndQuery)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"http://{store.HostName}{pathAndQuery}", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(CancellationToken))!;
    }

    private sealed record ProductPage(List<ProductSummary> Items, int TotalCount, List<Facet> Filters);

    private sealed record ProductSummary(string Slug, decimal Price);

    private sealed record Facet(string Code, string Type, List<FacetOption>? Options, JsonElement? Min, JsonElement? Max, int? TrueCount, int? FalseCount);

    private sealed record FacetOption(string Code, int Count, bool Selected);

    private sealed record ProductDetail(string Slug, List<ProductAttribute> Attributes);

    private sealed record ProductAttribute(string Code, string Name, string? Unit, JsonElement Value);

    private sealed record ProductValues(Dictionary<string, JsonElement> Values);
}

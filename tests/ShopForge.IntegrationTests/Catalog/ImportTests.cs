using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using ShopForge.Access.Domain;

namespace ShopForge.IntegrationTests.Catalog;

public sealed class ImportTests(ShopForgeApiFactory factory)
{
    private static readonly string[] Columns = ["sku", "name", "price", "vat", "categories", "material", "width", "visible"];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_creates_products_listings_categories_and_attribute_values()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var report = await ImportAsync(furniture, Columns,
            ["IMP-1", "Imported Oak Chair", 149.90m, 21m, "Chairs, Outdoor", "Oak", 42.5m, true],
            ["IMP-2", "Imported Beech Stool", 79m, 21m, "Chairs", "Beech", 30m, false]);

        var page = await GetAsync<ProductPage>(furniture, "/api/storefront/products?f.material=oak&f.width=40..45");
        var detail = await GetAsync<ProductDetail>(furniture, "/api/storefront/products/imported-oak-chair");
        var categories = await GetAsync<List<StorefrontCategory>>(furniture, "/api/storefront/categories");

        Assert.Equal((2, 0, 0, 0), (report.Created, report.Updated, report.Skipped, report.Invalid));
        Assert.Contains(page.Items, item => item.Slug == "imported-oak-chair" && item.Price == 149.90m);
        Assert.Equal(42.5m, detail.Attributes.Single(attribute => attribute.Code == "width").Value.GetDecimal());
        Assert.Contains(categories, category => category.Slug == "outdoor");
        Assert.DoesNotContain(page.Items, item => item.Slug == "imported-beech-stool");
    }

    [Fact]
    public async Task Importing_the_same_file_twice_changes_nothing()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        object?[][] rows = [["IMP-1", "Imported Chair", 149.90m, 21m, "Chairs", "Oak", 42.5m, true]];

        var first = await ImportAsync(furniture, Columns, rows);
        var second = await ImportAsync(furniture, Columns, rows);

        Assert.Equal((1, 0, 0), (first.Created, first.Updated, first.Skipped));
        Assert.Equal((0, 0, 1), (second.Created, second.Updated, second.Skipped));
    }

    [Fact]
    public async Task Import_updates_only_the_columns_it_contains()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var before = await GetAsync<ProductDetail>(furniture, "/api/storefront/products/oak-chair");

        var report = await ImportAsync(furniture, ["sku", "price"], [FurnitureStore.SkuOf("oak-chair"), 123.45m]);
        var after = await GetAsync<ProductDetail>(furniture, "/api/storefront/products/oak-chair");

        Assert.Equal((0, 1, 0, 0), (report.Created, report.Updated, report.Skipped, report.Invalid));
        Assert.Equal(123.45m, after.Price);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Attributes.Count, after.Attributes.Count);
    }

    [Fact]
    public async Task Invalid_rows_are_reported_and_the_rest_is_imported()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var report = await ImportAsync(furniture, ["sku", "name", "price", "vat", "width", "released"],
            ["IMP-OK", "Valid Chair", 10m, 21m, 40m, new DateOnly(2024, 5, 1)],
            ["", "No SKU", 10m, 21m, 40m, null],
            ["IMP-PRICE", "Bad Price", "cheap", 21m, 40m, null],
            ["IMP-NEW", null, 10m, 21m, 40m, null],
            ["IMP-WIDTH", "Bad Width", 10m, 21m, "wide", null],
            ["IMP-DATE", "Bad Date", 10m, 21m, 40m, "1st May"],
            ["IMP-OK", "Duplicate SKU", 10m, 21m, 40m, null]);

        Assert.Equal((1, 0, 0, 6), (report.Created, report.Updated, report.Skipped, report.Invalid));
        Assert.Equal(["price", "released", "sku", "sku", "width"], report.Issues.Where(issue => issue.Column != "name").Select(issue => issue.Column).Order());
        Assert.Contains(report.Issues, issue => issue.Column == "name" && issue.Message.Contains("not yet listed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_sets_stock_and_reports_unknown_columns()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var report = await ImportAsync(furniture, ["sku", "name", "price", "vat", "stock", "supplier"], ["IMP-1", "Chair", 10m, 21m, 5, "ACME"]);
        var page = await GetAsync<ProductPage>(furniture, "/api/storefront/products");

        Assert.Equal(1, report.Created);
        Assert.Equal(["supplier"], report.IgnoredColumns);
        Assert.Equal(5, page.Items.Single(item => item.Slug == "chair").Available);
    }

    [Fact]
    public async Task Import_creates_option_values_that_do_not_exist_yet()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var report = await ImportAsync(furniture, ["sku", "name", "price", "vat", "material", "colors"], ["IMP-1", "Teak Chair", 10m, 21m, "Teak", "Black, Green"]);
        var page = await GetAsync<ProductPage>(furniture, "/api/storefront/products?f.material=teak");

        Assert.Equal(1, report.Created);
        Assert.Equal("teak-chair", page.Items.Single().Slug);
        Assert.Contains(page.Filters.Single(filter => filter.Code == "colors").Options!, option => option.Code == "green");
    }

    [Fact]
    public async Task An_invalid_row_creates_neither_categories_nor_options()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var report = await ImportAsync(furniture, ["sku", "name", "price", "vat", "categories", "material"],
            ["IMP-GOOD", "Real Chair", 10m, 21m, "Chairs", "Oak"],
            ["IMP-BAD", "Ghost Chair", "free", 21m, "Ghost Category", "Phantom"]);
        var categories = await GetAsync<List<StorefrontCategory>>(furniture, "/api/storefront/categories");
        var page = await GetAsync<ProductPage>(furniture, "/api/storefront/products");

        Assert.Equal((1, 1), (report.Created, report.Invalid));
        Assert.DoesNotContain(categories, category => category.Slug == "ghost-category");
        Assert.DoesNotContain(page.Filters.Single(filter => filter.Code == "material").Options!, option => option.Code == "phantom");
    }

    [Theory]
    [InlineData(new[] { "name", "price" }, "sku")]
    [InlineData(new[] { "sku", "sku" }, "unique")]
    public async Task Files_without_usable_columns_are_rejected(string[] columns, string expected)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var response = await furniture.Admin.ImportAsync(furniture.Store.StoreId, ImportFiles.Workbook(columns, ["a", "b"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Files_that_are_not_workbooks_are_rejected()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var response = await furniture.Admin.ImportAsync(
            furniture.Store.StoreId, ImportFiles.Upload("sku;name;price"u8.ToArray(), "products.csv"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_needs_a_catalog_role_and_only_touches_its_own_store()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var support = await TestUsers.LoginAsync(factory, await TestUsers.CreateAsync(factory.Services, furniture.Store.TenantId, TenantRole.Support));

        using var forbidden = await support.ImportAsync(furniture.Store.StoreId, ImportFiles.Workbook(["sku", "name", "price", "vat"], ["IMP-1", "Chair", 10m, 21m]));
        await ImportAsync(furniture, ["sku", "name", "price", "vat"], ["IMP-1", "Chair", 10m, 21m]);
        var otherStore = await GetAsync<ProductPage>(furniture with { Store = furniture.OtherStore }, "/api/storefront/products");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Empty(otherStore.Items);
    }

    [Fact]
    public async Task Template_contains_the_fixed_columns_and_the_stores_attributes()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var response = await furniture.Admin.GetAsync($"/api/admin/stores/{furniture.Store.StoreId}/import/template", CancellationToken);
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync(CancellationToken));
        var headers = workbook.Worksheets.First().Row(1).Cells().Select(cell => cell.GetString()).ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("sku", headers);
        Assert.Contains("material", headers);
        Assert.Contains("width", headers);
    }

    private static async Task<ImportReport> ImportAsync(FurnitureStore furniture, string[] columns, params object?[][] rows)
    {
        using var response = await furniture.Admin.ImportAsync(furniture.Store.StoreId, ImportFiles.Workbook(columns, rows));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportReport>(CancellationToken))!;
    }

    private async Task<T> GetAsync<T>(FurnitureStore furniture, string pathAndQuery)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"http://{furniture.Store.HostName}{pathAndQuery}", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(CancellationToken))!;
    }

    private sealed record ImportReport(int Created, int Updated, int Skipped, int Invalid, int Failed, List<string> IgnoredColumns, List<Issue> Issues);

    private sealed record Issue(int Row, string? Column, string Message);

    private sealed record ProductPage(List<ProductSummary> Items, List<Facet> Filters);

    private sealed record ProductSummary(string Slug, decimal Price, int Available);

    private sealed record Facet(string Code, List<FacetOption>? Options);

    private sealed record FacetOption(string Code, int Count);

    private sealed record ProductDetail(string Slug, string Name, decimal Price, List<ProductAttribute> Attributes);

    private sealed record ProductAttribute(string Code, System.Text.Json.JsonElement Value);

    private sealed record StorefrontCategory(string Name, string Slug);
}

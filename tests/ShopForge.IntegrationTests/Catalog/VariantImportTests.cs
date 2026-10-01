using System.Net;
using System.Net.Http.Json;

namespace ShopForge.IntegrationTests.Catalog;

// A row of the file is one form of a product. Rows that share a listing are the same product, and an
// "option:size" column names the axis while its cells name each row's place along it (D-137).
public sealed class VariantImportTests(ShopForgeApiFactory factory)
{
    private static readonly string[] Columns = ["sku", "name", "price", "vat", "stock", "option:size"];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Three_rows_sharing_a_name_are_one_product_in_three_sizes()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var report = await ImportAsync(
            furniture,
            Columns,
            ["TEE-S", "Cotton Tee", 25m, 21m, 4, "S"],
            ["TEE-M", "Cotton Tee", 25m, 21m, 5, "M"],
            ["TEE-L", "Cotton Tee", 25m, 21m, 6, "L"]);

        var product = await ProductAsync(furniture, "TEE-S");
        var stock = await StockAsync(furniture);

        Assert.Empty(report.Issues);
        Assert.Equal(3, report.Created);
        Assert.Equal(["Size"], product.OptionNames);
        Assert.Equal(["TEE-S", "TEE-M", "TEE-L"], product.Variants.Select(variant => variant.Sku));
        Assert.Equal([["S"], ["M"], ["L"]], product.Variants.Select(variant => variant.OptionValues));
        Assert.Equal([4, 5, 6], product.Variants.Select(variant => stock[variant.Id]));
    }

    // Only one listing comes of it, because the three rows are three forms of one thing to sell.
    [Fact]
    public async Task Three_rows_make_one_listing_with_the_stock_added_up()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        await ImportAsync(
            furniture,
            Columns,
            ["TEE-S", "Cotton Tee", 25m, 21m, 4, "S"],
            ["TEE-M", "Cotton Tee", 25m, 21m, 5, "M"]);

        using var shopper = new Orders.StorefrontApi(factory, furniture.Store);
        var page = await shopper.GetJsonAsync<ProductPage>("/api/storefront/products");
        var detail = await shopper.GetJsonAsync<ProductDetail>("/api/storefront/products/cotton-tee");

        Assert.Single(page.Items, item => item.Slug == "cotton-tee");
        Assert.Equal(9, detail.Available);
        Assert.Equal([(["S"], 4), (["M"], 5)], detail.Variants.Select(variant => (variant.OptionValues, variant.Available)));
    }

    // A second import of the same file changes nothing, and a new row adds a form to the product already there.
    [Fact]
    public async Task A_later_file_adds_a_size_to_a_product_that_is_already_listed()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await ImportAsync(furniture, Columns, ["TEE-S", "Cotton Tee", 25m, 21m, 4, "S"]);

        var again = await ImportAsync(furniture, Columns, ["TEE-S", "Cotton Tee", 25m, 21m, 4, "S"]);
        var grown = await ImportAsync(furniture, Columns, ["TEE-XL", "Cotton Tee", 25m, 21m, 2, "XL"]);
        var product = await ProductAsync(furniture, "TEE-S");

        Assert.Equal(0, again.Created);
        Assert.Equal(1, grown.Created);
        Assert.Equal(["TEE-S", "TEE-XL"], product.Variants.Select(variant => variant.Sku));
        Assert.Equal([["S"], ["XL"]], product.Variants.Select(variant => variant.OptionValues));
    }

    [Fact]
    public async Task A_row_that_leaves_an_axis_blank_is_refused()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var report = await ImportAsync(
            furniture,
            Columns,
            ["TEE-S", "Cotton Tee", 25m, 21m, 4, "S"],
            ["TEE-M", "Cotton Tee", 25m, 21m, 5, ""]);

        Assert.Equal(1, report.Created);
        Assert.Equal(1, report.Invalid);
        Assert.Contains(report.Issues, issue => issue.Column == "option:size" && issue.Message.Contains("Size", StringComparison.Ordinal));
    }

    // A file that would rename the axes of a product already sold along different ones is refused, because the
    // forms it does not mention would be left with nothing to say for themselves.
    [Fact]
    public async Task A_file_cannot_rename_the_axes_of_a_product_already_sold_along_others()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await ImportAsync(furniture, Columns, ["TEE-S", "Cotton Tee", 25m, 21m, 4, "S"]);

        var report = await ImportAsync(
            furniture,
            ["sku", "name", "price", "vat", "option:colour"],
            ["TEE-S", "Cotton Tee", 25m, 21m, "Red"]);
        var product = await ProductAsync(furniture, "TEE-S");

        Assert.Equal(1, report.Invalid);
        Assert.Contains(report.Issues, issue => issue.Message.Contains("already sold along Size", StringComparison.Ordinal));
        Assert.Equal(["Size"], product.OptionNames);
    }

    // The column is part of the file's language, so it must not be reported back as one nobody understood.
    [Fact]
    public async Task An_option_column_is_not_an_unknown_column()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var report = await ImportAsync(
            furniture,
            ["sku", "name", "price", "vat", "option:size", "supplier"],
            ["TEE-S", "Cotton Tee", 25m, 21m, "S", "ACME"]);

        Assert.Equal(["supplier"], report.IgnoredColumns);
    }

    // A file with no option columns at all is what every file was before this, and still means one form.
    [Fact]
    public async Task A_file_without_option_columns_still_imports_a_plain_product()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var report = await ImportAsync(furniture, ["sku", "name", "price", "vat", "stock"], ["PLAIN-1", "Plain Thing", 9m, 21m, 3]);
        var product = await ProductAsync(furniture, "PLAIN-1");

        Assert.Equal(1, report.Created);
        Assert.Empty(product.OptionNames);
        Assert.Equal([[]], product.Variants.Select(variant => variant.OptionValues));
    }

    private async Task<ImportReport> ImportAsync(FurnitureStore furniture, string[] columns, params object?[][] rows)
    {
        using var response = await furniture.Admin.ImportAsync(furniture.Store.StoreId, ImportFiles.Workbook(columns, rows));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ImportReport>(CancellationToken))!;
    }

    private async Task<ProductView> ProductAsync(FurnitureStore furniture, string sku)
    {
        var products = await furniture.Admin.GetFromJsonAsync<List<ProductView>>("/api/admin/products", CancellationToken);

        return products!.Single(product => product.Variants.Any(variant => variant.Sku == sku));
    }

    private async Task<Dictionary<Guid, int>> StockAsync(FurnitureStore furniture)
    {
        var stock = await furniture.Admin.GetFromJsonAsync<List<StockView>>("/api/admin/stock", CancellationToken);

        return stock!.ToDictionary(item => item.VariantId, item => item.OnHand);
    }

    private sealed record ImportReport(int Created, int Updated, int Skipped, int Invalid, int Failed, List<string> IgnoredColumns, List<Issue> Issues);

    private sealed record Issue(int Row, string? Column, string Message);

    private sealed record ProductView(Guid Id, List<string> OptionNames, List<VariantView> Variants);

    private sealed record VariantView(Guid Id, string Sku, List<string> OptionValues);

    private sealed record StockView(Guid VariantId, int OnHand);

    private sealed record ProductPage(List<ProductSummary> Items);

    private sealed record ProductSummary(string Slug, int Available);

    private sealed record ProductDetail(int Available, List<VariantDetail> Variants);

    private sealed record VariantDetail(List<string> OptionValues, int Available);
}

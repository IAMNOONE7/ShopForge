using System.Net;
using System.Net.Http.Json;

namespace ShopForge.IntegrationTests.Catalog;

// The four things every shopping feed asks for and refuses a product without: a brand, a barcode that checks
// out, a manufacturer's part number and a condition. Nothing reads them until Stage 28 (D-163).
public sealed class FeedIdentifierTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_product_keeps_the_identifiers_a_feed_will_ask_for()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var created = await furniture.Admin.PostAsJsonAsync(
            "/api/admin/products",
            new
            {
                Sku = AdminCatalogApi.UniqueSku(),
                Ean = "5901234123457",
                WeightGrams = 850,
                Brand = "Thonet",
                PartNumber = "No-14-OAK",
                Condition = "Refurbished",
            },
            CancellationToken);
        var product = await created.Content.ReadFromJsonAsync<ProductView>(CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("Thonet", product!.Brand);
        Assert.Equal("5901234123457", product.Variants[0].Ean);
        Assert.Equal("No-14-OAK", product.Variants[0].PartNumber);
        Assert.Equal("Refurbished", product.Variants[0].Condition);
    }

    // A condition nobody has stated is not "new": a shop selling second-hand goods must not have them
    // declared new by a default nobody chose.
    [Fact]
    public async Task An_unstated_condition_stays_unstated()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var productId = await furniture.Admin.CreateProductAsync();
        var product = await LoadAsync(furniture, productId);

        Assert.Null(product.Brand);
        Assert.Null(product.Variants[0].Condition);
        Assert.Null(product.Variants[0].PartNumber);
    }

    // A feed rejects the whole product for a barcode whose check digit disagrees, so the shop refuses it first.
    [Theory]
    [InlineData("8594000000001")]
    [InlineData("590123412345")]
    [InlineData("12345")]
    [InlineData("590123412345x")]
    public async Task A_barcode_that_does_not_check_out_is_refused(string barcode)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var refused = await furniture.Admin.PostAsJsonAsync(
            "/api/admin/products",
            new { Sku = AdminCatalogApi.UniqueSku(), Ean = barcode },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_condition_that_is_not_one_of_the_three_is_refused()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var refused = await furniture.Admin.PostAsJsonAsync(
            "/api/admin/products",
            new { Sku = AdminCatalogApi.UniqueSku(), Condition = "slightly-used" },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // The brand belongs to the product, not to one of its sizes, so it is settable on a product sold in
    // several forms — which is the only place it could be set for such a product at all.
    [Fact]
    public async Task A_product_sold_in_several_forms_can_still_be_branded()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var productId = await furniture.Admin.CreateProductAsync();

        var first = (await LoadAsync(furniture, productId)).Variants[0].Id;
        using var options = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{productId}/options",
            new { Names = new[] { "Size" }, Values = new Dictionary<Guid, string[]> { [first] = ["Small"] } },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);

        using var second = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/products/{productId}/variants",
            new { Sku = AdminCatalogApi.UniqueSku(), OptionValues = new[] { "Large" } },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        using var branded = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{productId}", new { Brand = "Thonet" }, CancellationToken);
        using var alsoABarcode = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{productId}", new { Brand = "Thonet", Ean = "5901234123457" }, CancellationToken);
        var product = await LoadAsync(furniture, productId);

        Assert.Equal(HttpStatusCode.OK, branded.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, alsoABarcode.StatusCode);
        Assert.Equal("Thonet", product!.Brand);
    }

    [Fact]
    public async Task An_import_loads_the_identifiers_and_refuses_a_barcode_that_does_not_check_out()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        string[] columns = ["sku", "name", "price", "vat", "brand", "ean", "partnumber", "condition"];

        using var response = await furniture.Admin.ImportAsync(
            furniture.Store.StoreId,
            ImportFiles.Workbook(
                columns,
                ["FEED-1", "Branded Chair", 149.90m, 21m, "Thonet", "5901234123457", "No-14", "Used"],
                ["FEED-2", "Mistyped Chair", 99m, 21m, "Thonet", "5901234123456", "No-15", "New"]));
        var report = await response.Content.ReadFromJsonAsync<ImportReportView>(CancellationToken);

        var loaded = await furniture.Admin.GetFromJsonAsync<List<ProductView>>("/api/admin/products", CancellationToken);
        var branded = loaded!.Single(candidate => candidate.Sku == "FEED-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Thonet", branded.Brand);
        Assert.Equal("No-14", branded.Variants[0].PartNumber);
        Assert.Equal("Used", branded.Variants[0].Condition);
        Assert.Contains(report!.Issues, issue => issue.Column == "ean");
        Assert.DoesNotContain(loaded!, candidate => candidate.Sku == "FEED-2");
    }

    private static async Task<ProductView> LoadAsync(FurnitureStore furniture, Guid productId)
    {
        var products = await furniture.Admin.GetFromJsonAsync<List<ProductView>>("/api/admin/products", CancellationToken);

        return products!.Single(product => product.Id == productId);
    }

    private sealed record ProductView(Guid Id, string Sku, string? Brand, List<VariantView> Variants);

    private sealed record VariantView(Guid Id, string Sku, string? Ean, string? PartNumber, string? Condition);

    private sealed record ImportReportView(List<ImportIssueView> Issues);

    private sealed record ImportIssueView(int Row, string Column, string Message);
}

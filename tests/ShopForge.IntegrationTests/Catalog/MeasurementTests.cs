using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;

namespace ShopForge.IntegrationTests.Catalog;

// Every product is weighed and measured before delivery can be priced for it (D-214). The first step of V28b: the
// fields, an import that refuses anything incomplete, the export, the list of what is still unmeasured, and readiness
// advising about it. Refusing on create, visibility and publication waits until the admin's form can send sizes.
public sealed class MeasurementTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_product_carries_its_sizes_and_whether_it_ships_alone()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var created = await furniture.Admin.PostAsJsonAsync(
            "/api/admin/products",
            new { Sku = AdminCatalogApi.UniqueSku(), Ean = (string?)null, WeightGrams = 2400, LengthMm = 600, WidthMm = 400, HeightMm = 120, ShipsAlone = true },
            CancellationToken);
        var product = await created.Content.ReadFromJsonAsync<ProductView>(CancellationToken);
        var variant = product!.Variants.Single();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal((2400, 600, 400, 120, true), (product.WeightGrams, product.LengthMm, product.WidthMm, product.HeightMm, product.ShipsAlone));
        Assert.Equal((600, 400, 120, true), (variant.LengthMm, variant.WidthMm, variant.HeightMm, variant.ShipsAlone));
    }

    // The admin's form has no size fields yet, and a save from it must not erase what an import put there.
    [Fact]
    public async Task A_save_that_says_nothing_about_sizes_keeps_them()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var productId = furniture.ProductIds["oak-chair"];

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{productId}", new { Ean = (string?)null, WeightGrams = 900 }, CancellationToken);
        var kept = await saved.Content.ReadFromJsonAsync<ProductView>(CancellationToken);
        using var resized = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{productId}",
            new { Ean = (string?)null, WeightGrams = 900, LengthMm = 500, WidthMm = 450, HeightMm = 100, ShipsAlone = true },
            CancellationToken);
        var changed = await resized.Content.ReadFromJsonAsync<ProductView>(CancellationToken);

        Assert.Equal((900, 450, 400, 90, false), (kept!.WeightGrams, kept.LengthMm, kept.WidthMm, kept.HeightMm, kept.ShipsAlone));
        Assert.Equal((500, 450, 100, true), (changed!.LengthMm, changed.WidthMm, changed.HeightMm, changed.ShipsAlone));
    }

    [Theory]
    [InlineData(500, 400, null, "heightMm")]
    [InlineData(null, 400, 100, "lengthMm")]
    [InlineData(0, 400, 100, "lengthMm")]
    [InlineData(500, 10_001, 100, "widthMm")]
    public async Task Sizes_come_all_three_together_and_each_within_ten_metres(int? length, int? width, int? height, string field)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var refused = await furniture.Admin.PostAsJsonAsync(
            "/api/admin/products",
            new { Sku = AdminCatalogApi.UniqueSku(), Ean = (string?)null, WeightGrams = 900, LengthMm = length, WidthMm = width, HeightMm = height },
            CancellationToken);
        var problem = await refused.Content.ReadFromJsonAsync<ProblemView>(CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.True(problem!.Errors.ContainsKey(field), string.Join(", ", problem.Errors.Keys));
    }

    // A product sold in several forms has no single size: each form carries its own, and the product refuses one.
    [Fact]
    public async Task Each_form_of_a_product_is_measured_on_its_own()
    {
        var shirt = await ShirtStore.CreateAsync(factory);
        var admin = shirt.Furniture.Admin;
        var large = (await ProductAsync(admin, shirt.ProductId)).Variants.Single(variant => variant.Id == shirt.Large);

        using var onTheProduct = await admin.PutAsJsonAsync(
            $"/api/admin/products/{shirt.ProductId}", new { LengthMm = 300, WidthMm = 200, HeightMm = 20 }, CancellationToken);
        using var onTheForm = await admin.PutAsJsonAsync(
            $"/api/admin/products/{shirt.ProductId}/variants/{shirt.Large}",
            new { large.Sku, Ean = (string?)null, WeightGrams = 270, OptionValues = new[] { "L" }, LengthMm = 330, WidthMm = 260, HeightMm = 35 },
            CancellationToken);
        var product = await ProductAsync(admin, shirt.ProductId);

        Assert.Equal(HttpStatusCode.BadRequest, onTheProduct.StatusCode);
        Assert.Equal(HttpStatusCode.OK, onTheForm.StatusCode);
        Assert.Equal((330, 260, 35), Sizes(product.Variants.Single(variant => variant.Id == shirt.Large)));
        Assert.Equal((450, 400, 90), Sizes(product.Variants.Single(variant => variant.Id == shirt.Small)));
    }

    // The owner's rule: a product without a weight or its sizes does not come in (D-214).
    [Fact]
    public async Task An_import_refuses_a_new_product_without_a_weight_or_its_sizes()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var report = await ImportAsync(
            furniture,
            ImportFiles.Workbook(
                ["sku", "name", "price", "vat", "weight", "package_length_mm", "package_width_mm", "package_height_mm"],
                ["BARE-1", "Bare Chair", 10m, 21m, null, null, null, null],
                ["HALF-1", "Half Chair", 10m, 21m, 900, 500, null, null],
                ["NOUGHT-1", "Weightless Chair", 10m, 21m, 0, 500, 400, 300]));
        var skus = (await ProductsAsync(furniture.Admin)).Select(product => product.Sku);

        Assert.Equal((0, 3), (report.Created, report.Invalid));
        Assert.Contains(report.Issues, issue => issue is { Row: 2, Column: "weight" });
        Assert.Contains(report.Issues, issue => issue.Row == 2 && issue.Column == "package_length_mm" && issue.Message.Contains("package_width_mm", StringComparison.Ordinal));
        Assert.Contains(report.Issues, issue => issue.Row == 3 && issue.Column == "package_width_mm" && issue.Message.Contains("package_height_mm", StringComparison.Ordinal));
        Assert.Contains(report.Issues, issue => issue is { Row: 4, Column: "weight" });
        Assert.DoesNotContain("BARE-1", skus);
        Assert.DoesNotContain("HALF-1", skus);
        Assert.DoesNotContain("NOUGHT-1", skus);
    }

    [Fact]
    public async Task An_import_brings_a_measured_product_in_and_keeps_what_a_row_leaves_out()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var created = await ImportAsync(
            furniture,
            ImportFiles.Workbook(
                ["sku", "name", "price", "vat", "weight", "package_length_mm", "package_width_mm", "package_height_mm", "ships_alone"],
                ["BOXED-1", "Boxed Drill", 1490m, 21m, 2600, 420, 330, 110, "yes"]));
        var restated = await ImportAsync(
            furniture,
            ImportFiles.Workbook(["sku", "price"], ["BOXED-1", 1490m], [FurnitureStore.SkuOf("oak-chair"), 100m]));
        var boxed = (await ProductsAsync(furniture.Admin)).Single(product => product.Sku == "BOXED-1");

        Assert.Equal((1, 0), (created.Created, created.Invalid));
        Assert.Equal((2600, 420, 330, 110, true), (boxed.WeightGrams, boxed.LengthMm, boxed.WidthMm, boxed.HeightMm, boxed.ShipsAlone));
        Assert.Equal((0, 2, 0), (restated.Updated, restated.Skipped, restated.Invalid));
    }

    [Theory]
    [InlineData("package_length_mm", "ten")]
    [InlineData("package_width_mm", 0)]
    [InlineData("package_height_mm", 10_001)]
    [InlineData("ships_alone", "perhaps")]
    public async Task An_import_names_a_cell_it_cannot_read(string column, object cell)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        string[] columns = ["sku", "name", "price", "vat", "weight", "package_length_mm", "package_width_mm", "package_height_mm", "ships_alone"];
        object?[] row = ["CELL-1", "Cell Chair", 10m, 21m, 900, 500, 400, 300, "no"];
        row[Array.IndexOf(columns, column)] = cell;

        var report = await ImportAsync(furniture, ImportFiles.Workbook(columns, row));

        Assert.Equal((0, 1), (report.Created, report.Invalid));
        Assert.Contains(report.Issues, issue => issue.Row == 2 && issue.Column == column);
    }

    // What the export writes is what the import reads, so a catalogue goes out and back unchanged.
    [Fact]
    public async Task The_export_carries_the_sizes_and_goes_back_in_unchanged()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var file = await furniture.Admin.GetByteArrayAsync($"/api/admin/stores/{furniture.Store.StoreId}/export", CancellationToken);
        using var workbook = new XLWorkbook(new MemoryStream(file));
        var sheet = workbook.Worksheets.First();
        var headers = sheet.Row(1).CellsUsed().Select(cell => cell.GetString()).ToList();
        var oakRow = sheet.RowsUsed().Skip(1).Single(row => row.Cell(1).GetString() == FurnitureStore.SkuOf("oak-chair"));
        var reimported = await ImportAsync(furniture, ImportFiles.Upload(file, "export.xlsx"));

        Assert.Equal(["weight", "package_length_mm", "package_width_mm", "package_height_mm", "ships_alone"], headers.Skip(headers.IndexOf("weight")).Take(5));
        Assert.Contains("width", headers);
        Assert.Equal("450", oakRow.Cell(headers.IndexOf("package_length_mm") + 1).GetString());
        Assert.Equal((0, 0, 0, furniture.Products.Count), (reimported.Created, reimported.Updated, reimported.Invalid, reimported.Skipped));
    }

    // The forms still to finish, and only the company's own.
    [Fact]
    public async Task The_unmeasured_list_names_what_each_form_lacks_and_no_other_companys()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var other = await FurnitureStore.CreateAsync(factory);
        var unsized = await CreateAsync(furniture.Admin, new { Sku = "UNSIZED-1", Ean = (string?)null, WeightGrams = 900 });
        var weightless = await CreateAsync(furniture.Admin, new { Sku = "WEIGHTLESS-1", Ean = (string?)null, WeightGrams = 0, LengthMm = 100, WidthMm = 100, HeightMm = 100 });
        await CreateAsync(other.Admin, new { Sku = "OTHERS-1", Ean = (string?)null, WeightGrams = (int?)null });

        var list = await furniture.Admin.GetFromJsonAsync<UnmeasuredView>("/api/admin/products/unmeasured", CancellationToken);
        var paged = await furniture.Admin.GetFromJsonAsync<UnmeasuredView>("/api/admin/products/unmeasured?page=2&pageSize=1", CancellationToken);

        Assert.Equal(2, list!.Total);
        Assert.Equal(["UNSIZED-1", "WEIGHTLESS-1"], list.Variants.Select(variant => variant.Sku));
        Assert.Equal(unsized, list.Variants[0].ProductId);
        Assert.Equal(["lengthMm", "widthMm", "heightMm"], list.Variants[0].Missing);
        Assert.Equal(weightless, list.Variants[1].ProductId);
        Assert.Equal(["weightGrams"], list.Variants[1].Missing);
        Assert.Equal(["WEIGHTLESS-1"], paged!.Variants.Select(variant => variant.Sku));
    }

    // Advice for now, never a refusal: the admin's form cannot send sizes yet (V28b's second step).
    [Fact]
    public async Task Readiness_advises_about_a_visible_product_that_is_not_measured()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var storeId = furniture.Store.StoreId;

        var measured = await CheckAsync(furniture);
        var productId = await CreateAsync(furniture.Admin, new { Sku = AdminCatalogApi.UniqueSku(), Ean = (string?)null, WeightGrams = 900 });
        await furniture.Admin.ListProductAsync(storeId, productId, "Unsized Lamp", 10m);
        var weightless = await CreateAsync(
            furniture.Admin, new { Sku = AdminCatalogApi.UniqueSku(), Ean = (string?)null, WeightGrams = (int?)null, LengthMm = 300, WidthMm = 300, HeightMm = 300 });
        await furniture.Admin.ListProductAsync(storeId, weightless, "Weightless Lamp", 10m);
        await furniture.Admin.ListProductAsync(storeId, await CreateAsync(furniture.Admin, new { Sku = AdminCatalogApi.UniqueSku(), Ean = (string?)null, WeightGrams = (int?)null }), "Hidden Lamp", 10m, isVisible: false);

        var advised = await CheckAsync(furniture);

        Assert.True(measured.IsReady);
        Assert.Equal("advised", measured.Weight);
        Assert.False(advised.IsReady);
        Assert.Equal("advised", advised.Weight);
        Assert.Contains("Unsized Lamp", advised.Problem!, StringComparison.Ordinal);
        Assert.Contains("Weightless Lamp", advised.Problem!, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden Lamp", advised.Problem!, StringComparison.Ordinal);
    }

    private static (int?, int?, int?) Sizes(VariantView variant) => (variant.LengthMm, variant.WidthMm, variant.HeightMm);

    private static async Task<Guid> CreateAsync(HttpClient admin, object body)
    {
        using var response = await admin.PostAsJsonAsync("/api/admin/products", body, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ProductView>(CancellationToken))!.Id;
    }

    private static async Task<ProductView> ProductAsync(HttpClient admin, Guid productId) =>
        (await ProductsAsync(admin)).Single(product => product.Id == productId);

    private static async Task<List<ProductView>> ProductsAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<List<ProductView>>("/api/admin/products", CancellationToken))!;

    private static async Task<ImportReport> ImportAsync(FurnitureStore furniture, MultipartFormDataContent file)
    {
        using var response = await furniture.Admin.ImportAsync(furniture.Store.StoreId, file);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ImportReport>(CancellationToken))!;
    }

    private static async Task<CheckView> CheckAsync(FurnitureStore furniture)
    {
        var readiness = await furniture.Admin.GetFromJsonAsync<ReadinessView>(
            $"/api/admin/stores/{furniture.Store.StoreId}/readiness", CancellationToken);

        return readiness!.Checks.Single(check => check.Check == "catalog.shipping-data");
    }

    private sealed record ProductView(
        Guid Id, string Sku, int? WeightGrams, int? LengthMm, int? WidthMm, int? HeightMm, bool ShipsAlone, List<VariantView> Variants);

    private sealed record VariantView(Guid Id, string Sku, int? WeightGrams, int? LengthMm, int? WidthMm, int? HeightMm, bool ShipsAlone);

    private sealed record ProblemView(Dictionary<string, string[]> Errors);

    private sealed record ImportReport(int Created, int Updated, int Skipped, int Invalid, int Failed, List<Issue> Issues);

    private sealed record Issue(int Row, string? Column, string Message);

    private sealed record UnmeasuredView(int Total, List<UnmeasuredVariantView> Variants);

    private sealed record UnmeasuredVariantView(Guid ProductId, Guid VariantId, string Sku, List<string> Missing);

    private sealed record ReadinessView(bool IsReady, List<CheckView> Checks);

    private sealed record CheckView(string Check, bool IsReady, string? Problem, string Weight);
}

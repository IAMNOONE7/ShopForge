using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Shared.Admin;

namespace ShopForge.IntegrationTests.Admin;

// A merchant can take their data out, and what comes out of the catalogue goes back in (D-185).
public sealed class ExportTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_catalogue_comes_out_in_the_columns_the_importer_reads()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var sheet = await CatalogueAsync(furniture);
        var headers = Headers(sheet);
        var template = await TemplateHeadersAsync(furniture);

        // Every column the template offers is a column the export fills, in the same order.
        Assert.Equal(template, headers.Take(template.Count));
    }

    [Fact]
    public async Task Every_form_of_every_thing_is_a_row_of_its_own()
    {
        var shirts = await ShirtStore.CreateAsync(factory);

        var sheet = await CatalogueAsync(shirts.Furniture);
        var rows = Rows(sheet);
        var shirtRows = rows.Where(row => row["slug"] == shirts.Slug).ToList();

        // The shirt is sold in two sizes, so it is two rows that share a name and differ by code.
        Assert.Equal(2, shirtRows.Count);
        Assert.Single(shirtRows.Select(row => row["name"]).Distinct());
        Assert.Equal(2, shirtRows.Select(row => row["sku"]).Distinct().Count());

        // This shop sells one thing along an axis and the rest along none, so the file does not try to
        // describe the axes at all — the importer would read them as every product's (D-185).
        Assert.DoesNotContain("option:size", Headers(sheet));
    }

    [Fact]
    public async Task What_a_listing_is_comes_out_with_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var oak = Rows(await CatalogueAsync(furniture)).Single(row => row["slug"] == "oak-chair");

        Assert.Equal("Oak Chair", oak["name"]);
        Assert.Equal(FurnitureStore.SkuOf("oak-chair"), oak["sku"]);
        Assert.Equal("100", oak["price"]);
        Assert.Equal("chairs", oak["categories"]);
        Assert.Equal("oak", oak["material"]);
        Assert.Equal("45", oak["width"]);
    }

    // A thing in two categories names both, separated the way the importer reads them back. One category
    // each would let any separator look right.
    [Fact]
    public async Task A_listing_in_two_categories_names_both()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var sale = await furniture.Admin.CreateCategoryAsync(furniture.Store.StoreId, "Sale");
        using var assigned = await furniture.Admin.AssignCategoriesAsync(
            furniture.Store.StoreId, furniture.Products["oak-chair"], furniture.ChairsCategoryId, sale);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        var oak = Rows(await CatalogueAsync(furniture)).Single(row => row["slug"] == "oak-chair");
        var exported = await CatalogueBytesAsync(furniture);
        var report = await ImportAsync(furniture, exported);

        Assert.Equal("chairs, sale", oak["categories"]);
        Assert.Equal(0, report.Invalid);
        Assert.Empty(report.Issues);
    }

    // When everything in the shop is sold along the same axes, the file carries them and they come back.
    [Fact]
    public async Task A_shop_whose_things_share_their_axes_carries_them_and_takes_them_back()
    {
        var shirts = await ShirtStore.CreateAsync(factory);
        var furniture = shirts.Furniture;

        // Leave only the thing that is sold in sizes, so the catalogue speaks along one axis.
        foreach (var slug in new[] { "oak-chair", "walnut-chair", "beech-stool", "oak-bench" })
        {
            using var archived = await furniture.Admin.PostAsync(
                $"/api/admin/stores/{furniture.Store.StoreId}/products/{furniture.Products[slug]}/archive", null, CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        }

        var sheet = await CatalogueAsync(furniture);
        var rows = Rows(sheet);
        var report = await ImportAsync(furniture, await CatalogueBytesAsync(furniture));

        Assert.Contains("option:size", Headers(sheet));
        Assert.Equal(["S", "L"], rows.Select(row => row["option:size"]));
        Assert.Equal(0, report.Invalid);
        Assert.Empty(report.Issues);
    }

    // A shop that sells different things along different axes exports faithfully and cannot import the
    // result: the file format describes one set of axes for the whole file (D-137), so there is no file that
    // is right for a chair sold along nothing and a shirt sold along size at once. The export says what is
    // true; the importer says plainly why it will not take it back (D-185).
    [Fact]
    public async Task A_shop_with_mixed_axes_exports_faithfully_and_is_told_why_it_cannot_import_it()
    {
        var shirts = await ShirtStore.CreateAsync(factory);

        var sheet = await CatalogueAsync(shirts.Furniture);
        var report = await ImportAsync(shirts.Furniture, await CatalogueBytesAsync(shirts.Furniture));

        // Everything is in the file, and both forms of the shirt are rows of their own.
        Assert.Equal(6, Rows(sheet).Count);
        Assert.DoesNotContain("option:size", Headers(sheet));

        // And the refusal names the thing that cannot be described rather than failing quietly.
        Assert.NotEqual(0, report.Invalid);
        Assert.All(
            report.Issues,
            issue => Assert.Contains("already sold along", issue.Message, StringComparison.Ordinal));
    }

    // The round trip the slice exists for: out, changed in a spreadsheet, and back in.
    [Fact]
    public async Task An_export_edited_in_a_spreadsheet_imports_back()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var exported = await CatalogueBytesAsync(furniture);
        var edited = WithChange(exported, "oak-chair", "name", "Oak Dining Chair");

        var report = await ImportAsync(furniture, edited);

        // Nothing invalid is the assertion that matters: a file the exporter wrote must be one the importer
        // accepts whole. The first version of this failed here, because a date went out in the machine's own
        // format and came back unreadable (D-185).
        Assert.Equal(0, report.Invalid);
        Assert.Empty(report.Issues);
        Assert.Equal(1, report.Updated);
        var listings = await furniture.Admin.AdminPageAsync<ListingRow>(
            $"/api/admin/stores/{furniture.Store.StoreId}/products?pageSize=200", CancellationToken);

        Assert.Equal(0, report.Failed);
        Assert.Equal("Oak Dining Chair", listings.Items.Single(listing => listing.Slug == "oak-chair").Name);
        Assert.Equal(4, listings.TotalCount);
    }

    // Stock is the warehouse's answer, not the catalogue's, and an empty cell means "leave it alone" — so a
    // round trip must not quietly set everything to nothing.
    [Fact]
    public async Task A_round_trip_leaves_the_stock_where_it_was()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var exported = await CatalogueBytesAsync(furniture);

        await ImportAsync(furniture, exported);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var page = await shopper.GetJsonAsync<ProductDetail>("/api/storefront/products/oak-chair");

        Assert.Equal(FurnitureStore.StockPerProduct, page.Available);
    }

    [Fact]
    public async Task One_stores_catalogue_is_not_anothers()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var neighbour = await FurnitureStore.CreateAsync(factory);

        var ours = Rows(await CatalogueAsync(furniture));

        Assert.Equal(4, ours.Count);
        Assert.NotEqual(furniture.Store.StoreId, neighbour.Store.StoreId);
    }

    // An archived listing is out of use, and an export is what a merchant works from.
    [Fact]
    public async Task An_archived_listing_is_not_in_the_export()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var archived = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{furniture.Products["walnut-chair"]}/archive", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);

        var rows = Rows(await CatalogueAsync(furniture));

        Assert.DoesNotContain("walnut-chair", rows.Select(row => row["slug"]));
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task Orders_come_out_with_what_an_accountant_asks_for()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var number = await PlaceOrderAsync(furniture);

        var rows = Rows(await OrdersAsync(furniture, ""));
        var order = rows.Single(row => row["number"] == number);

        Assert.Equal("buyer@example.test", order["email"]);
        Assert.Equal("AwaitingPayment", order["status"]);
        Assert.Equal("EUR", order["currency"]);
        Assert.NotEqual(string.Empty, order["total"]);
        Assert.NotEqual(string.Empty, order["billing name"]);
    }

    // A merchant exporting for one month wants one month.
    [Fact]
    public async Task Orders_can_be_asked_for_by_the_window_they_were_placed_in()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await PlaceOrderAsync(furniture);

        var thisYear = Rows(await OrdersAsync(furniture, "?from=2026-01-01"));
        var longAgo = Rows(await OrdersAsync(furniture, "?from=2020-01-01&to=2020-12-31"));

        Assert.NotEmpty(thisYear);
        Assert.Empty(longAgo);
    }

    [Fact]
    public async Task One_stores_orders_are_not_anothers()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var neighbour = await FurnitureStore.CreateAsync(factory);
        var ourOrder = await PlaceOrderAsync(furniture);

        var theirs = Rows(await OrdersAsync(neighbour, ""));

        Assert.DoesNotContain(ourOrder, theirs.Select(row => row["number"]));
    }

    [Fact]
    public async Task An_export_needs_the_right_to_see_what_it_exports()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var stranger = await TestUsers.LoginAsync(
            factory, await TestUsers.CreateAsync(factory.Services, furniture.Store.TenantId, ShopForge.Access.Domain.TenantRole.Support));

        using var catalogue = await stranger.GetAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/export", CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, catalogue.StatusCode);
    }

    private async Task<IXLWorksheet> CatalogueAsync(FurnitureStore furniture) =>
        Open(await CatalogueBytesAsync(furniture));

    private async Task<byte[]> CatalogueBytesAsync(FurnitureStore furniture)
    {
        using var response = await furniture.Admin.GetAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/export", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsByteArrayAsync(CancellationToken);
    }

    private async Task<IXLWorksheet> OrdersAsync(FurnitureStore furniture, string query)
    {
        using var response = await furniture.Admin.GetAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/export{query}", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return Open(await response.Content.ReadAsByteArrayAsync(CancellationToken));
    }

    private async Task<List<string>> TemplateHeadersAsync(FurnitureStore furniture)
    {
        using var response = await furniture.Admin.GetAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/import/template", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return Headers(Open(await response.Content.ReadAsByteArrayAsync(CancellationToken)));
    }

    private async Task<ImportReportView> ImportAsync(FurnitureStore furniture, byte[] file)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(content, "file", "catalogue.xlsx");

        using var imported = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/import", form, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);

        return (await imported.Content.ReadFromJsonAsync<ImportReportView>(CancellationToken))!;
    }

    private async Task<string> PlaceOrderAsync(FurnitureStore furniture)
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        return (await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created)).Number;
    }

    private static IXLWorksheet Open(byte[] file)
    {
        var workbook = new XLWorkbook(new MemoryStream(file));

        return workbook.Worksheet(1);
    }

    private static List<string> Headers(IXLWorksheet sheet) =>
        [.. sheet.Row(1).CellsUsed().Select(cell => cell.GetString())];

    // The sheet as rows keyed by their header, which is how a merchant reads it and how a test should.
    private static List<Dictionary<string, string>> Rows(IXLWorksheet sheet)
    {
        var headers = Headers(sheet);
        var rows = new List<Dictionary<string, string>>();

        for (var row = 2; row <= sheet.LastRowUsed()?.RowNumber(); row++)
        {
            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var column = 0; column < headers.Count; column++)
            {
                cells[headers[column]] = sheet.Cell(row, column + 1).GetString();
            }

            rows.Add(cells);
        }

        return rows;
    }

    // One cell changed, the way a merchant would change it.
    private static byte[] WithChange(byte[] file, string slug, string column, string value)
    {
        using var workbook = new XLWorkbook(new MemoryStream(file));
        var sheet = workbook.Worksheet(1);
        var headers = Headers(sheet);
        var slugColumn = headers.IndexOf("slug") + 1;
        var target = headers.IndexOf(column) + 1;

        for (var row = 2; row <= sheet.LastRowUsed()!.RowNumber(); row++)
        {
            if (sheet.Cell(row, slugColumn).GetString() == slug)
            {
                sheet.Cell(row, target).Value = value;
            }
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    private sealed record ListingRow(Guid Id, string Name, string Slug);

    private sealed record ImportReportView(int Created, int Updated, int Skipped, int Invalid, int Failed, List<string> IgnoredColumns, List<IssueView> Issues);

    private sealed record IssueView(int Row, string? Column, string Message);

    private sealed record ProductDetail(string Slug, int Available, List<VariantView> Variants);

    private sealed record VariantView(Guid Id);
}

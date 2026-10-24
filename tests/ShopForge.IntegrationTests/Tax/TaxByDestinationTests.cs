using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.Access.Domain;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Domain;

namespace ShopForge.IntegrationTests.Tax;

// A shop whose home is Czechia selling into Germany and Ireland. What a thing attracts is the destination's
// business, not the shop's (D-187).
public sealed class TaxByDestinationTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_destination_with_its_own_rate_is_charged_that_rate()
    {
        var furniture = await BooksStoreAsync();

        var german = await OrderAsync(furniture, "DE");

        Assert.Equal([7m], (await LinesAsync(furniture, german)).Select(line => line.VatRate).Distinct());
    }

    // The shop said nothing about Ireland, so a parcel going there is charged what the shop charges at home,
    // which is what a shop not registered where it is sending does.
    [Fact]
    public async Task A_destination_nobody_has_priced_is_charged_the_shop_own_rate()
    {
        var furniture = await BooksStoreAsync();

        var irish = await OrderAsync(furniture, "IE");

        Assert.Equal([10m], (await LinesAsync(furniture, irish)).Select(line => line.VatRate).Distinct());
    }

    // The Done-when of the stage: two countries, two rates, and the documents right in both.
    [Fact]
    public async Task One_shop_selling_to_two_countries_invoices_each_at_its_own_rate()
    {
        var furniture = await BooksStoreAsync();

        var german = await PaidOrderAsync(furniture, "DE");
        var irish = await PaidOrderAsync(furniture, "IE");

        var germanInvoice = await InvoiceAsync(furniture, german);
        var irishInvoice = await InvoiceAsync(furniture, irish);

        // 100 gross at 7 % is 6.54 of tax; at 10 % it is 9.09. The gross is the same both times, because what
        // the shopper was shown is what the shopper pays and the destination decides how much of it is tax
        // (D-187). The courier names no class, so it stays on 21 % and makes a second row — which is what a
        // VAT summary is for.
        Assert.Equal([(7m, 93.46m, 6.54m, 100m), (21m, 4.05m, 0.85m, 4.90m)], Rows(germanInvoice));
        Assert.Equal([(10m, 90.91m, 9.09m, 100m), (21m, 4.05m, 0.85m, 4.90m)], Rows(irishInvoice));
        Assert.Equal(germanInvoice.Total, irishInvoice.Total);
    }

    [Fact]
    public async Task A_listing_that_names_no_class_keeps_its_rate_wherever_it_goes()
    {
        var furniture = await BooksStoreAsync();

        // The walnut chair was never pointed at a class, so it stays on the 21 % it was listed with.
        var order = await OrderAsync(furniture, "DE", product: "walnut-chair");

        Assert.Equal([21m], (await LinesAsync(furniture, order)).Select(line => line.VatRate).Distinct());
    }

    [Fact]
    public async Task A_delivery_is_taxed_where_the_parcel_is_going_too()
    {
        var furniture = await BooksStoreAsync();
        using var pointed = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/shipping-methods/courier",
            new { Name = "Courier", Price = 4.90m, VatRate = 21m, IsActive = true, RequiresPickupPoint = false, TaxClass = "books" },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, pointed.StatusCode);

        var german = await OrderAsync(furniture, "DE");
        var order = await OrderRowAsync(furniture, german);

        Assert.Equal(7m, order.ShippingVatRate);
    }

    [Fact]
    public async Task A_listing_cannot_name_a_class_the_shop_has_not_got()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var response = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{furniture.Products["oak-chair"]}",
            new { Name = "Oak Chair", Price = 100m, VatRate = 21m, IsVisible = true, SortOrder = 0, TaxClass = "invented" },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not one this store has", await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    // D-180: what holds a thing says so, and says how much of it there is.
    [Fact]
    public async Task A_class_something_is_taxed_as_cannot_be_deleted_and_the_refusal_counts_them()
    {
        var furniture = await BooksStoreAsync();

        using var refused = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/tax-classes/books", CancellationToken);
        var detail = await refused.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("1 listing is taxed as it", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_class_nothing_names_is_deleted_with_its_rates()
    {
        var furniture = await BooksStoreAsync();
        await ClassAsync(furniture, "food", "Food");
        await RateAsync(furniture, "food", "DE", 7m);

        using var deleted = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/tax-classes/food", CancellationToken);
        var remaining = await furniture.Admin.GetFromJsonAsync<List<TaxClassView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/tax-classes", CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(["books"], remaining!.Select(taxClass => taxClass.Code));
    }

    [Fact]
    public async Task Setting_a_rate_is_recorded_and_setting_it_to_what_it_was_is_not()
    {
        var furniture = await BooksStoreAsync();
        var before = await EntriesAsync(furniture, "store.tax_rate.set");

        await RateAsync(furniture, "books", "DE", 7m);
        var unchanged = await EntriesAsync(furniture, "store.tax_rate.set");

        await RateAsync(furniture, "books", "DE", 5m);
        var changed = await EntriesAsync(furniture, "store.tax_rate.set");

        Assert.Equal(before, unchanged);
        Assert.Equal(before + 1, changed);
    }

    // A shop that starts selling abroad has thousands of things to classify, so it can say it once (D-183).
    [Fact]
    public async Task Many_listings_are_pointed_at_a_class_at_once()
    {
        var furniture = await BooksStoreAsync();
        List<Guid> chosen = [furniture.Products["walnut-chair"], furniture.Products["beech-stool"]];

        using var response = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/bulk/tax-class",
            new { StoreProductIds = chosen, TaxClass = "books" },
            CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<BulkResultView>(CancellationToken);

        var order = await OrderAsync(furniture, "DE", product: "walnut-chair");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((2, 2), (result!.Asked, result.Changed));
        Assert.Equal([7m], (await LinesAsync(furniture, order)).Select(line => line.VatRate).Distinct());
    }

    [Fact]
    public async Task Many_listings_cannot_be_pointed_at_a_class_the_shop_has_not_got()
    {
        var furniture = await BooksStoreAsync();

        using var response = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/bulk/tax-class",
            new { StoreProductIds = new[] { furniture.Products["walnut-chair"] }, TaxClass = "invented" },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // A workbook that says nothing about tax is not an instruction to undo what the admin set. Before D-201 any
    // import row that touched a classified listing put it back on its own rate, and a book sent to Germany was
    // charged 21 %.
    [Fact]
    public async Task An_import_that_names_no_class_leaves_a_listings_class_alone()
    {
        var furniture = await BooksStoreAsync();

        var report = await ImportAsync(furniture, ["sku", "name", "price"], [FurnitureStore.SkuOf("oak-chair"), "Oak Reading Chair", 100m]);
        var german = await OrderAsync(furniture, "DE");

        Assert.Equal((1, 0), (report.Updated, report.Invalid));
        Assert.Equal("books", (await ListingAsync(furniture, "oak-chair")).TaxClass);
        Assert.Equal([7m], (await LinesAsync(furniture, german)).Select(line => line.VatRate).Distinct());
    }

    // An empty cell leaves a value alone in every column, so a row that restates the listing changes nothing.
    [Fact]
    public async Task A_row_that_restates_a_classified_listing_changes_nothing()
    {
        var furniture = await BooksStoreAsync();

        var report = await ImportAsync(furniture, ["sku", "price", "taxclass"], [FurnitureStore.SkuOf("oak-chair"), 100m, null]);

        Assert.Equal((0, 1), (report.Updated, report.Skipped));
        Assert.Equal("books", (await ListingAsync(furniture, "oak-chair")).TaxClass);
    }

    // Codes are lower case, and a spreadsheet is where somebody types "Books" (D-201).
    [Fact]
    public async Task The_workbook_can_say_what_a_listing_is_for_tax()
    {
        var furniture = await BooksStoreAsync();

        var report = await ImportAsync(furniture, ["sku", "taxclass"], [FurnitureStore.SkuOf("walnut-chair"), "Books"]);
        var german = await OrderAsync(furniture, "DE", product: "walnut-chair");
        var recorded = await factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<string>($"SELECT details AS \"Value\" FROM auditing.audit_entries WHERE store_id = {furniture.Store.StoreId} AND action = 'catalog.import.applied'")
            .SingleAsync(CancellationToken));

        Assert.Equal((1, 0), (report.Updated, report.Invalid));
        Assert.Contains("\"reclassified\":1", recorded, StringComparison.Ordinal);
        Assert.Equal("books", (await ListingAsync(furniture, "walnut-chair")).TaxClass);
        Assert.Equal([7m], (await LinesAsync(furniture, german)).Select(line => line.VatRate).Distinct());
    }

    [Fact]
    public async Task A_product_the_import_brings_in_has_a_class_only_when_the_file_names_one()
    {
        var furniture = await BooksStoreAsync();

        var report = await ImportAsync(furniture, ["sku", "name", "price", "vat", "taxclass"],
            ["IMP-ATLAS", "Atlas", 30m, 21m, "books"],
            ["IMP-GLOBE", "Globe", 30m, 21m, null]);

        Assert.Equal((2, 0), (report.Created, report.Invalid));
        Assert.Equal("books", (await ListingAsync(furniture, "atlas")).TaxClass);
        Assert.Null((await ListingAsync(furniture, "globe")).TaxClass);
    }

    // The same rule the admin keeps, with the store's classes named, because the person reading the report is
    // looking at a spreadsheet rather than at a list of classes.
    [Fact]
    public async Task A_class_the_shop_has_not_got_is_refused_and_the_row_left_as_it_was()
    {
        var furniture = await BooksStoreAsync();

        var report = await ImportAsync(furniture, ["sku", "price", "taxclass"], [FurnitureStore.SkuOf("walnut-chair"), 1m, "invented"]);
        var issue = Assert.Single(report.Issues);
        var walnut = await ListingAsync(furniture, "walnut-chair");

        Assert.Equal((0, 1), (report.Updated, report.Invalid));
        Assert.Equal("taxclass", issue.Column);
        Assert.Equal("The tax class 'invented' is not one this store has: it has books.", issue.Message);
        Assert.Equal((200m, null), (walnut.Price, walnut.TaxClass));
    }

    [Fact]
    public async Task A_rate_outside_nought_to_a_hundred_is_refused()
    {
        var furniture = await BooksStoreAsync();

        using var response = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/tax-classes/books/rates/DE",
            new { Rate = 120m },
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Whoever keeps the catalogue says which class each listing is, so they can read the classes there are. What a
    // class is, and what each country charges for it, stays with whoever runs the store (D-204).
    [Fact]
    public async Task The_catalogue_staff_read_the_classes_and_change_none_of_them()
    {
        var furniture = await BooksStoreAsync();
        using var catalogue = await TestUsers.LoginAsync(
            factory, await TestUsers.CreateAsync(factory.Services, furniture.Store.TenantId, TenantRole.CatalogManager));
        using var warehouse = await TestUsers.LoginAsync(
            factory, await TestUsers.CreateAsync(factory.Services, furniture.Store.TenantId, TenantRole.Warehouse));
        var classes = $"/api/admin/stores/{furniture.Store.StoreId}/tax-classes";

        var read = await catalogue.GetFromJsonAsync<List<TaxClassView>>(classes, CancellationToken);
        using var created = await catalogue.PostAsJsonAsync(classes, new { Name = "Food", Code = "food" }, CancellationToken);
        using var renamed = await catalogue.PutAsJsonAsync($"{classes}/books", new { Name = "Printed books" }, CancellationToken);
        using var priced = await catalogue.PutAsJsonAsync($"{classes}/books/rates/IE", new { Rate = 0m }, CancellationToken);
        using var unpriced = await catalogue.DeleteAsync($"{classes}/books/rates/DE", CancellationToken);
        using var deleted = await catalogue.DeleteAsync($"{classes}/books", CancellationToken);
        using var warehouseRead = await warehouse.GetAsync(classes, CancellationToken);
        var after = await furniture.Admin.GetFromJsonAsync<List<TaxClassView>>(classes, CancellationToken);

        var books = Assert.Single(read!);
        Assert.Equal(("books", "Books"), (books.Code, books.Name));
        Assert.Equal([("CZ", 10m), ("DE", 7m)], books.Rates.Select(rate => (rate.Country, rate.Rate)).Order());
        Assert.All(
            [created, renamed, priced, unpriced, deleted],
            response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        Assert.Equal(HttpStatusCode.Forbidden, warehouseRead.StatusCode);
        Assert.Equivalent(read, after, strict: true);
    }

    private static List<(decimal Rate, decimal Net, decimal Vat, decimal Gross)> Rows(Invoice invoice) =>
        [.. invoice.VatSummary().Select(row => (row.Rate, row.Net, row.Vat, row.Gross))];

    private async Task<FurnitureStore> BooksStoreAsync()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        await ClassAsync(furniture, "books", "Books");
        await RateAsync(furniture, "books", "CZ", 10m);
        await RateAsync(furniture, "books", "DE", 7m);

        using var pointed = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{furniture.Products["oak-chair"]}",
            new { Name = "Oak Chair", Price = 100m, VatRate = 21m, IsVisible = true, SortOrder = 0, TaxClass = "books" },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, pointed.StatusCode);

        return furniture;
    }

    private async Task ClassAsync(FurnitureStore furniture, string code, string name)
    {
        using var response = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/tax-classes",
            new { Name = name, Code = code },
            CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task RateAsync(FurnitureStore furniture, string code, string country, decimal rate)
    {
        using var response = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/tax-classes/{code}/rates/{country}",
            new { Rate = rate },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<string> OrderAsync(FurnitureStore furniture, string country, string product = "oak-chair")
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products[product], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", CheckoutTo(country));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);

        return order.Number;
    }

    private async Task<string> PaidOrderAsync(FurnitureStore furniture, string country)
    {
        var number = await OrderAsync(furniture, country);

        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        await factory.EventuallyAsync(
            () => factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Invoice>()
                .CountAsync(invoice => invoice.OrderNumber == number, CancellationToken)),
            issued => issued > 0,
            CancellationToken);

        return number;
    }

    private static object CheckoutTo(string country) => new
    {
        Email = "buyer@example.test",
        Phone = "+420 123 456 789",
        BillingAddress = new { FullName = "Alex Buyer", Line1 = "1 Main Street", Line2 = (string?)null, City = "Town", PostalCode = "12345", Country = country },
        ShippingAddress = (object?)null,
        PaymentMethodCode = "bank-transfer",
        ShippingMethodCode = "courier",
        PickupPointCode = (string?)null,
    };

    private Task<List<OrderLine>> LinesAsync(FurnitureStore furniture, string number) =>
        factory.QueryAsync(furniture.Store, async dbContext => (await dbContext.Set<Order>()
            .AsNoTracking()
            .Include(order => order.Lines)
            .SingleAsync(order => order.Number == number, CancellationToken)).Lines.ToList());

    private Task<Order> OrderRowAsync(FurnitureStore furniture, string number) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(order => order.Number == number, CancellationToken));

    private Task<Invoice> InvoiceAsync(FurnitureStore furniture, string number) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Invoice>()
            .Include(invoice => invoice.Lines)
            .SingleAsync(invoice => invoice.OrderNumber == number, CancellationToken));

    private Task<int> EntriesAsync(FurnitureStore furniture, string action) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM auditing.audit_entries WHERE store_id = {furniture.Store.StoreId} AND action = {action}")
            .SingleAsync(CancellationToken));

    private static async Task<ImportReportView> ImportAsync(FurnitureStore furniture, string[] columns, params object?[][] rows)
    {
        using var response = await furniture.Admin.ImportAsync(furniture.Store.StoreId, ImportFiles.MeasuredWorkbook(columns, rows));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ImportReportView>(CancellationToken))!;
    }

    private static async Task<ListingView> ListingAsync(FurnitureStore furniture, string slug)
    {
        var listings = await furniture.Admin.AdminListAsync<ListingView>(
            $"/api/admin/stores/{furniture.Store.StoreId}/products?pageSize=200", CancellationToken);

        return listings.Single(listing => listing.Slug == slug);
    }

    private sealed record BulkResultView(int Asked, int Changed);

    private sealed record TaxClassView(string Code, string Name, List<TaxRateView> Rates);

    private sealed record ImportReportView(int Created, int Updated, int Skipped, int Invalid, List<IssueView> Issues);

    private sealed record IssueView(int Row, string? Column, string Message);

    private sealed record ListingView(string Slug, decimal Price, string? TaxClass);

    private sealed record TaxRateView(string Country, decimal Rate);
}

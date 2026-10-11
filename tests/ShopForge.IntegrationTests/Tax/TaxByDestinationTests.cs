using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
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
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM auditing.audit_entries WHERE action = {action}")
            .SingleAsync(CancellationToken));

    private sealed record BulkResultView(int Asked, int Changed);

    private sealed record TaxClassView(string Code, string Name, List<TaxRateView> Rates);

    private sealed record TaxRateView(string Country, decimal Rate);
}

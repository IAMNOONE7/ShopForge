using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Domain;

namespace ShopForge.IntegrationTests.Invoicing;

public sealed class InvoiceTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Paying_an_order_issues_an_invoice_with_its_vat_summary()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var order = await PaidOrderAsync(furniture, shopper, "oak-chair", quantity: 2);

        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");
        var document = Assert.Single(confirmation.Documents);
        var pdf = await DownloadAsync(shopper, $"/api/storefront/orders/{order.Number}/documents/{document.Number}?token={order.Token}");

        Assert.Equal("Invoice", document.Kind);
        Assert.Matches(@"^INV-\d{4}-\d{5}$", document.Number);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf[..4]), StringComparison.Ordinal);
    }

    // The order was 2 × 100 at 21 % plus 4.90 shipping at 21 %: one rate, one row in the summary.
    [Fact]
    public async Task The_invoice_keeps_the_amounts_the_order_was_paid_with()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var order = await PaidOrderAsync(furniture, shopper, "oak-chair", quantity: 2);
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");

        var invoice = await InvoiceAsync(furniture, confirmation.Documents[0].Number);
        var summary = invoice.VatSummary().Single();

        Assert.Equal(204.90m, invoice.Total);
        Assert.Equal(35.56m, invoice.VatTotal);
        Assert.Equal(169.34m, invoice.NetTotal);
        Assert.Equal((21m, 169.34m, 35.56m, 204.90m), (summary.Rate, summary.Net, summary.Vat, summary.Gross));
        Assert.Equal("Test Furniture s.r.o.", invoice.Seller.LegalName);
        Assert.Equal(["Oak Chair", "Courier"], invoice.Lines.Select(line => line.Description));
    }

    [Fact]
    public async Task An_invoice_is_issued_once_however_often_the_event_arrives()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var order = await PaidOrderAsync(furniture, shopper, "walnut-chair", quantity: 1);

        await factory.DispatchOutboxAsync(CancellationToken);
        await RedeliverPaymentEventAsync(furniture, order.Number);
        await factory.DispatchOutboxAsync(CancellationToken);
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");

        Assert.Single(confirmation.Documents);
    }

    [Fact]
    public async Task Only_the_customer_and_the_store_can_download_a_document()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var order = await PaidOrderAsync(furniture, shopper, "beech-stool", quantity: 1);
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");
        var number = confirmation.Documents[0].Number;

        using var wrongToken = await shopper.GetAsync($"/api/storefront/orders/{order.Number}/documents/{number}?token={Guid.NewGuid()}");
        using var admin = await furniture.Admin.GetAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/documents/{number}", CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, wrongToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal("application/pdf", admin.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task The_store_sees_the_documents_on_the_order()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var order = await PaidOrderAsync(furniture, shopper, "oak-chair", quantity: 1);

        var detail = await furniture.Admin.GetFromJsonAsync<AdminOrderView>(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}", CancellationToken);
        using var refunded = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);
        var afterRefund = (await refunded.Content.ReadFromJsonAsync<AdminOrderView>(CancellationToken))!;

        Assert.Equal(["Invoice"], detail!.Documents.Select(document => document.Kind));
        Assert.Equal(["Invoice", "CreditNote"], afterRefund.Documents.Select(document => document.Kind));
    }

    [Fact]
    public async Task Refunding_returns_the_stock_and_issues_a_credit_note()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var productId = furniture.ProductIds["oak-bench"];
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var order = await PaidOrderAsync(furniture, shopper, "oak-bench", quantity: 2);
        var afterSale = await StockAsync(furniture, productId);

        using var refunded = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);
        using var again = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");
        var afterRefund = await StockAsync(furniture, productId);

        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("Refunded", confirmation.Status);
        Assert.Equal(afterSale.OnHand + 2, afterRefund.OnHand);
        Assert.Contains(confirmation.Documents, document => document.Kind == "CreditNote" && document.Number.StartsWith("CN-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_credit_note_shows_what_is_being_given_back()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var order = await PaidOrderAsync(furniture, shopper, "oak-chair", quantity: 1);
        using var refunded = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);

        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");
        var creditNote = confirmation.Documents.Single(document => document.Kind == "CreditNote");
        var pdf = await DownloadAsync(shopper, $"/api/storefront/orders/{order.Number}/documents/{creditNote.Number}?token={order.Token}");

        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        Assert.Equal(2, confirmation.Documents.Count);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf[..4]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Document_numbers_run_per_store_and_series()
    {
        var first = await FurnitureStore.CreateAsync(factory);
        var second = await FurnitureStore.CreateAsync(factory);
        using var firstShopper = new StorefrontApi(factory, first.Store);
        using var secondShopper = new StorefrontApi(factory, second.Store);

        var firstOrder = await PaidOrderAsync(first, firstShopper, "oak-chair", quantity: 1);
        var secondOrder = await PaidOrderAsync(second, secondShopper, "oak-chair", quantity: 1);
        var firstDocuments = (await firstShopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{firstOrder.Number}?token={firstOrder.Token}")).Documents;
        var secondDocuments = (await secondShopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{secondOrder.Number}?token={secondOrder.Token}")).Documents;
        var year = DateTimeOffset.UtcNow.Year;

        Assert.Equal($"INV-{year}-00001", firstDocuments.Single().Number);
        Assert.Equal($"INV-{year}-00001", secondDocuments.Single().Number);
    }

    private async Task<PlacedOrder> PaidOrderAsync(FurnitureStore furniture, StorefrontApi shopper, string product, int quantity)
    {
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items",
            new { StoreProductId = furniture.Products[product], Quantity = quantity });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);

        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        await factory.DispatchOutboxAsync(CancellationToken);

        return order;
    }

    private static async Task<byte[]> DownloadAsync(StorefrontApi shopper, string path)
    {
        using var response = await shopper.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsByteArrayAsync(CancellationToken);
    }

    private Task RedeliverPaymentEventAsync(FurnitureStore furniture, string orderNumber) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Database.ExecuteSqlAsync(
            $"""
            UPDATE messaging.outbox_messages
            SET status = 'Pending', due_at = now(), processed_at = NULL
            WHERE store_id = {furniture.Store.StoreId} AND type = 'order.paid' AND payload LIKE {'%' + orderNumber + '%'}
            """,
            CancellationToken));

    private async Task<StockView> StockAsync(FurnitureStore furniture, Guid productId)
    {
        var stock = await furniture.Admin.GetFromJsonAsync<List<StockView>>("/api/admin/stock", CancellationToken);

        return stock!.Single(item => item.ProductId == productId);
    }

    // Read back as the module sees it, so the totals and the VAT summary are the ones the document is built from.
    private Task<Invoice> InvoiceAsync(FurnitureStore furniture, string documentNumber) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<Invoice>()
            .SingleAsync(invoice => invoice.Number == documentNumber, CancellationToken));

    private sealed record StockView(Guid ProductId, int OnHand, int Reserved, int Available);

    private sealed record AdminOrderView(string Number, string Status, List<DocumentView> Documents);
}

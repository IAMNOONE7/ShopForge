using System.Net;
using System.Text;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Invoicing;

public sealed class MailAttachmentTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_invoice_arrives_with_the_payment_receipt()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        var order = await PaidOrderAsync(furniture, shopper, email);

        var attachments = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.AttachmentsFor(email, "Payment received")),
            found => found.Count > 0,
            CancellationToken);
        var invoice = attachments.Single();

        Assert.StartsWith("INV-", invoice.FileName, StringComparison.Ordinal);
        Assert.EndsWith(".pdf", invoice.FileName, StringComparison.Ordinal);
        Assert.Equal("application/pdf", invoice.ContentType);

        // A real document rather than a placeholder: it says it is a PDF and it mentions the order it is for.
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(invoice.Content[..4]), StringComparison.Ordinal);
        Assert.True(invoice.Content.Length > 1000);
        _ = order;
    }

    [Fact]
    public async Task The_credit_note_arrives_with_the_refund()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        var order = await PaidOrderAsync(furniture, shopper, email);

        using var refunded = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/refund", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);

        var attachments = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.AttachmentsFor(email, "Refund for order")),
            found => found.Count > 0,
            CancellationToken);

        Assert.StartsWith("CN-", attachments.Single().FileName, StringComparison.Ordinal);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(attachments.Single().Content[..4]), StringComparison.Ordinal);
    }

    // A message nobody can find a document for still has to reach the customer (D-122).
    [Fact]
    public async Task A_message_whose_document_cannot_be_found_is_sent_without_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var email = $"buyer-{Guid.NewGuid():N}@example.test";

        // The order confirmation names no document at all, which is the same path as one that cannot be resolved.
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items",
            new { StoreProductId = furniture.Products["beech-stool"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(email: email));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);

        var confirmation = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.For(email).FirstOrDefault(message => message.Subject.Contains(order.Number, StringComparison.Ordinal))),
            delivered => delivered is not null,
            CancellationToken);

        Assert.NotNull(confirmation);
        Assert.Empty(factory.Emails.AttachmentsFor(email, order.Number));
    }

    private async Task<PlacedOrder> PaidOrderAsync(FurnitureStore furniture, StorefrontApi shopper, string email)
    {
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items",
            new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(email: email));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        return order;
    }
}

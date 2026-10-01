using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Orders.Domain;

namespace ShopForge.IntegrationTests.Invoicing;

// What the customer reads has to say which one they bought. The order line keeps the name as written, and the
// invoice, the credit note, the return and the export all read it from there (D-136).
public sealed class VariantPaperworkTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_order_of_two_sizes_says_which_is_which_all_the_way_to_the_invoice()
    {
        var shirt = await ShirtStore.CreateAsync(factory);
        await shirt.StockAsync(small: 5, large: 5);

        using var buyer = await BuyerAsync(shirt);
        await AddAsync(buyer, shirt, shirt.Small, 1);
        await AddAsync(buyer, shirt, shirt.Large, 2);
        var order = await CheckoutAsync(buyer);
        await PayAsync(shirt, order.Number);

        var confirmation = await buyer.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");
        var invoice = await InvoiceAsync(shirt, order.Number);

        Assert.Equal(["Linen Shirt (S)", "Linen Shirt (L)"], confirmation.Lines.Select(line => line.ProductName));
        Assert.Equal(["Linen Shirt (S)", "Linen Shirt (L)", "Courier"], invoice.Lines.Select(line => line.Description));
    }

    // A thing sold in one form reads exactly as it always did: no empty brackets on the invoice.
    [Fact]
    public async Task A_product_sold_in_one_form_is_named_the_way_it_always_was()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        var order = await CheckoutAsync(shopper);
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");

        Assert.Equal(["Oak Chair"], confirmation.Lines.Select(line => line.ProductName));
    }

    // Sending one size back credits that size by name, and offers the other one as still returnable.
    [Fact]
    public async Task A_credit_note_names_the_size_that_came_back()
    {
        var shirt = await ShirtStore.CreateAsync(factory);
        await shirt.StockAsync(small: 5, large: 5);

        using var buyer = await BuyerAsync(shirt);
        await AddAsync(buyer, shirt, shirt.Small, 1);
        await AddAsync(buyer, shirt, shirt.Large, 2);
        var order = await CheckoutAsync(buyer);
        await PayAsync(shirt, order.Number);

        await ReturnAsync(shirt, buyer, order.Number, shirt.Large, 2);
        var creditNote = (await DocumentsAsync(shirt, order.Number, InvoiceKind.CreditNote)).Single();
        var afterwards = await buyer.GetJsonAsync<CustomerReturnsView>($"/api/storefront/account/orders/{order.Number}/returns");

        Assert.Equal(["Linen Shirt (L)"], creditNote.Lines.Select(line => line.Description));
        Assert.Equal(["Linen Shirt (S)"], afterwards.Returnable.Select(line => line.ProductName));
        Assert.Equal(["Linen Shirt (L)"], afterwards.Returns.Single().Lines.Select(line => line.ProductName));
    }

    // The name is written once, at the moment of the order, so renaming the size afterwards cannot rewrite
    // paperwork a customer already has.
    [Fact]
    public async Task Renaming_the_size_afterwards_does_not_rewrite_the_order()
    {
        var shirt = await ShirtStore.CreateAsync(factory);
        await shirt.StockAsync(small: 5, large: 5);

        using var shopper = new StorefrontApi(factory, shirt.Furniture.Store);
        await AddAsync(shopper, shirt, shirt.Large, 1);
        var order = await CheckoutAsync(shopper);

        using var renamed = await shirt.Furniture.Admin.PutAsJsonAsync(
            $"/api/admin/products/{shirt.ProductId}/variants/{shirt.Large}",
            new { Sku = "RENAMED-L", Ean = (string?)null, WeightGrams = (int?)null, OptionValues = new[] { "Extra Large" } },
            CancellationToken);
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(["Linen Shirt (L)"], confirmation.Lines.Select(line => line.ProductName));
    }

    private async Task<Invoice> InvoiceAsync(ShirtStore shirt, string orderNumber)
    {
        var invoices = await factory.EventuallyAsync(
            () => DocumentsAsync(shirt, orderNumber, InvoiceKind.Invoice),
            found => found.Count > 0,
            CancellationToken);

        return invoices.Single();
    }

    private Task<List<Invoice>> DocumentsAsync(ShirtStore shirt, string orderNumber, InvoiceKind kind) =>
        factory.QueryAsync(shirt.Furniture.Store, async dbContext => await dbContext.Set<Invoice>()
            .Where(invoice => invoice.OrderNumber == orderNumber && invoice.Kind == kind)
            .OrderBy(invoice => invoice.Number)
            .ToListAsync(CancellationToken));

    private static async Task AddAsync(StorefrontApi shopper, ShirtStore shirt, Guid variantId, int quantity)
    {
        using var response = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = shirt.Listing, VariantId = variantId, Quantity = quantity });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PlacedOrder> CheckoutAsync(StorefrontApi shopper)
    {
        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        return await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
    }

    private async Task PayAsync(ShirtStore shirt, string number)
    {
        using var paid = await shirt.Furniture.Admin.PostAsync(
            $"/api/admin/stores/{shirt.Furniture.Store.StoreId}/orders/{number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
    }

    private async Task ReturnAsync(ShirtStore shirt, StorefrontApi buyer, string orderNumber, Guid variantId, int quantity)
    {
        using var requested = await buyer.PostAsync(
            $"/api/storefront/account/orders/{orderNumber}/returns",
            new { Lines = new[] { new { StoreProductId = shirt.Listing, VariantId = variantId, Quantity = quantity } }, Reason = "Too big." });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);

        var returns = await shirt.Furniture.Admin.GetFromJsonAsync<List<ReturnRow>>(
            $"/api/admin/stores/{shirt.Furniture.Store.StoreId}/returns", CancellationToken);
        var booked = returns!.Single(candidate => candidate.OrderNumber == orderNumber).Id;

        foreach (var decision in new[] { "accept", "receive" })
        {
            using var response = await shirt.Furniture.Admin.PostAsync(
                $"/api/admin/stores/{shirt.Furniture.Store.StoreId}/returns/{booked}/{decision}", null, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private async Task<StorefrontApi> BuyerAsync(ShirtStore shirt)
    {
        var email = $"paperwork-{Guid.NewGuid():N}@example.test";
        var buyer = new StorefrontApi(factory, shirt.Furniture.Store);

        await buyer.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password = "Shop-forge-2026", FirstName = "Robin", LastName = "Sender", Phone = (string?)null });
        var token = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.NextLinkFor(email)),
            link => link is not null,
            CancellationToken);
        using var verified = await buyer.PostAsync("/api/storefront/account/verify", new { Token = token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        return buyer;
    }

    private sealed record OrderView(List<OrderLineView> Lines);

    private sealed record OrderLineView(string ProductName, int Quantity);

    private sealed record ReturnRow(Guid Id, string OrderNumber);

    private sealed record CustomerReturnsView(List<ReturnableView> Returnable, List<CustomerReturnView> Returns);

    private sealed record ReturnableView(string ProductName, int Quantity);

    private sealed record CustomerReturnView(List<ReturnLineView> Lines);

    private sealed record ReturnLineView(string ProductName, int Quantity);
}

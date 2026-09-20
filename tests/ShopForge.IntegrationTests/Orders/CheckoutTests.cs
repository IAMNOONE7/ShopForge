using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Orders;

public sealed class CheckoutTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Placing_an_order_returns_payment_instructions_and_empties_the_cart()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 2);

        var order = await PlaceOrderAsync(shopper);
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");
        var cart = await shopper.GetJsonAsync<CartView>("/api/storefront/cart");

        Assert.Matches(@"^\d{4}-\d{5}$", order.Number);
        Assert.Contains(order.Number, order.PaymentInstructions, StringComparison.Ordinal);
        Assert.Equal(("AwaitingPayment", "buyer@example.test", "EUR"), (confirmation.Status, confirmation.Email, confirmation.Currency));
        Assert.Equal(2, confirmation.Lines.Single().Quantity);
        Assert.Empty(cart.Items);
    }

    [Fact]
    public async Task Totals_cover_items_shipping_and_vat()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var product = await furniture.Admin.CreateProductAsync();
        var storeProductId = await furniture.Admin.ListProductAsync(furniture.Store.StoreId, product, "Priced Chair", 121m);
        await furniture.Admin.StockAsync(product, 10);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, storeProductId, 2);

        var order = await PlaceOrderAsync(shopper);
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");

        Assert.Equal(242m, confirmation.ItemsTotal);
        Assert.Equal(4.90m, confirmation.ShippingPrice);
        Assert.Equal(246.90m, confirmation.GrandTotal);
        Assert.Equal(42.85m, confirmation.VatTotal);
    }

    [Fact]
    public async Task An_order_keeps_the_prices_it_was_placed_with()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chair = furniture.Products["oak-chair"];
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, chair, 1);
        var order = await PlaceOrderAsync(shopper);

        using var priceChange = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{chair}",
            new { Name = "Oak Chair", Slug = "oak-chair", Price = 999m, VatRate = 21m, IsVisible = true, SortOrder = 0 },
            CancellationToken);
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");

        Assert.Equal(HttpStatusCode.OK, priceChange.StatusCode);
        Assert.Equal(100m, confirmation.Lines.Single().UnitPrice);
    }

    [Fact]
    public async Task Order_numbers_run_per_store()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var otherProduct = await furniture.Admin.CreateProductAsync();
        var otherListing = await furniture.Admin.ListProductAsync(furniture.OtherStore.StoreId, otherProduct, "Lamp", 10m);
        await furniture.Admin.StockAsync(otherProduct, 10);
        using var firstShopper = new StorefrontApi(factory, furniture.Store);
        using var secondShopper = new StorefrontApi(factory, furniture.Store);
        using var otherStoreShopper = new StorefrontApi(factory, furniture.OtherStore);

        await AddToCartAsync(firstShopper, furniture.Products["oak-chair"], 1);
        var first = await PlaceOrderAsync(firstShopper);
        await AddToCartAsync(secondShopper, furniture.Products["beech-stool"], 1);
        var second = await PlaceOrderAsync(secondShopper);
        await AddToCartAsync(otherStoreShopper, otherListing, 1);
        var otherStoreOrder = await PlaceOrderAsync(otherStoreShopper);

        var year = DateTimeOffset.UtcNow.Year;
        Assert.Equal($"{year}-00001", first.Number);
        Assert.Equal($"{year}-00002", second.Number);
        Assert.Equal($"{year}-00001", otherStoreOrder.Number);
    }

    [Fact]
    public async Task An_order_is_only_readable_with_its_token()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        var order = await PlaceOrderAsync(shopper);

        using var wrongToken = await shopper.GetAsync($"/api/storefront/orders/{order.Number}?token={Guid.NewGuid()}");
        using var otherStore = new StorefrontApi(factory, furniture.OtherStore);
        using var acrossStores = await otherStore.GetAsync($"/api/storefront/orders/{order.Number}?token={order.Token}");

        Assert.Equal(HttpStatusCode.NotFound, wrongToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, acrossStores.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email", "bank-transfer", "courier", "email")]
    [InlineData("buyer@example.test", "invented", "courier", "paymentMethodCode")]
    [InlineData("buyer@example.test", "bank-transfer", "invented", "shippingMethodCode")]
    public async Task Checkout_rejects_incomplete_requests(string email, string payment, string shipping, string field)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);

        using var response = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(email, payment, shipping));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_cart_cannot_be_ordered()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        using var response = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Orders_are_listed_for_their_own_store_only()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 3);
        var order = await PlaceOrderAsync(shopper);

        var ownOrders = await furniture.Admin.GetFromJsonAsync<List<AdminOrder>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders", CancellationToken);
        var otherStoreOrders = await furniture.Admin.GetFromJsonAsync<List<AdminOrder>>(
            $"/api/admin/stores/{furniture.OtherStore.StoreId}/orders", CancellationToken);
        var detail = await furniture.Admin.GetFromJsonAsync<AdminOrderDetail>(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}", CancellationToken);

        Assert.Equal([order.Number], ownOrders!.Select(candidate => candidate.Number));
        Assert.Empty(otherStoreOrders!);
        Assert.Equal(("Alex Buyer", "Dublin"), (detail!.BillingAddress.FullName, detail.ShippingAddress.City));
        Assert.Equal(3, detail.Lines.Single().Quantity);
    }

    [Fact]
    public async Task A_store_without_an_active_payment_method_cannot_be_published()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var deactivated = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/payment-methods/bank-transfer",
            new { Name = "Bank transfer", IsActive = false },
            CancellationToken);
        using var publish = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/publish", null, CancellationToken);
        var problems = (await publish.Content.ReadFromJsonAsync<PublishProblems>(CancellationToken))!.Problems;

        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, publish.StatusCode);
        Assert.Contains(problems, problem => problem.Contains("payment method", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task AddToCartAsync(StorefrontApi shopper, Guid storeProductId, int quantity)
    {
        using var response = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = storeProductId, Quantity = quantity });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PlacedOrder> PlaceOrderAsync(StorefrontApi shopper)
    {
        using var response = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        return await shopper.ReadAsync<PlacedOrder>(response, HttpStatusCode.Created);
    }

    private sealed record AdminOrder(string Number, string Status, string Email, decimal GrandTotal, int Items);

    private sealed record AdminOrderDetail(string Number, AddressView BillingAddress, AddressView ShippingAddress, List<OrderLineView> Lines);

    private sealed record AddressView(string FullName, string City);

    private sealed record PublishProblems(List<string> Problems);
}

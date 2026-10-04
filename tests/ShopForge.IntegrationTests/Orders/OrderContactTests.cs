using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.Orders.Domain;

namespace ShopForge.IntegrationTests.Orders;

// Two things every carrier asks for that the order used to go without: somebody to telephone, and which carrier
// it was placed with (D-144, D-081).
public sealed class OrderContactTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_order_without_a_telephone_number_is_refused()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);

        using var missing = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(phone: null));
        using var blank = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(phone: "   "));
        using var prose = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(phone: "call the office"));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, prose.StatusCode);
    }

    // The shop takes the number as the shopper writes it: spacing and a country code are how people give one,
    // and what a carrier will accept is the carrier's rule to state rather than ours to guess at.
    [Theory]
    [InlineData("+420 123 456 789")]
    [InlineData("777888999")]
    [InlineData("(01) 234-5678")]
    public async Task A_number_written_the_way_people_write_them_is_kept(string phone)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(phone: phone));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);

        Assert.Equal(phone, await StoredAsync(furniture, order.Number, stored => stored.Phone));
    }

    // Whoever picks the order up in the warehouse needs the number, and the staff are who would ring it.
    [Fact]
    public async Task The_admin_sees_the_number_the_shopper_gave()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        var order = await PlaceOrderAsync(shopper);

        var seen = await furniture.Admin.GetFromJsonAsync<AdminOrderView>(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}", CancellationToken);

        Assert.Equal("+420 123 456 789", seen!.Phone);
    }

    [Fact]
    public async Task An_order_remembers_which_carrier_it_was_placed_with()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        var order = await PlaceOrderAsync(shopper);

        Assert.Equal("manual", await StoredAsync(furniture, order.Number, stored => stored.ShippingProviderKey));
    }

    // The method is the store's to rename, reprice or withdraw, and an order already placed is not. Shipping
    // used to ask the method who its carrier was, so withdrawing one stranded every order that had used it.
    [Fact]
    public async Task A_method_withdrawn_after_the_order_still_ships_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        var order = await PlaceOrderAsync(shopper);
        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        await WithdrawCourierAsync(furniture);
        using var shipped = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/shipment",
            new { TrackingNumber = "PKG-1" },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, shipped.StatusCode);
    }

    private Task<T> StoredAsync<T>(FurnitureStore furniture, string number, Func<Order, T> read) =>
        factory.QueryAsync(furniture.Store, async dbContext => read(await dbContext.Set<Order>()
            .AsNoTracking()
            .SingleAsync(order => order.Number == number, CancellationToken)));

    // There is no endpoint for this: a method is withdrawn by the store, and what matters here is the order
    // outliving the row rather than how the row went.
    private Task WithdrawCourierAsync(FurnitureStore furniture) =>
        factory.QueryAsync(furniture.Store, dbContext => dbContext.Set<ShippingMethod>()
            .Where(method => method.Code == "courier")
            .ExecuteDeleteAsync(CancellationToken));

    private static async Task<PlacedOrder> PlaceOrderAsync(StorefrontApi shopper)
    {
        using var response = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());

        return await shopper.ReadAsync<PlacedOrder>(response, HttpStatusCode.Created);
    }

    private static async Task AddToCartAsync(StorefrontApi shopper, Guid storeProductId, int quantity)
    {
        using var response = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = storeProductId, Quantity = quantity });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record AdminOrderView(string Number, string? Phone);
}

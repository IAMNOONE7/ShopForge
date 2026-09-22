using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Orders;

public sealed class ShippingTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_method_that_needs_a_pickup_point_refuses_an_order_without_one()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await AddPickupPointAsync(furniture, "Main Shop");
        await AddCollectionMethodAsync(furniture);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);

        using var withoutPoint = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(shipping: "collection"));
        using var withUnknownPoint = await shopper.PostAsync(
            "/api/storefront/checkout",
            Checkout.Request(shipping: "collection", pickupPoint: "somewhere-else"));

        Assert.Equal(HttpStatusCode.BadRequest, withoutPoint.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withUnknownPoint.StatusCode);
    }

    [Fact]
    public async Task The_chosen_pickup_point_stays_with_the_order()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await AddPickupPointAsync(furniture, "Main Shop");
        await AddCollectionMethodAsync(furniture);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);

        var order = await PlaceOrderAsync(shopper, "main-shop");
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");

        Assert.Equal("Main Shop, 4 Market Square, Galway", confirmation.PickupPoint);
    }

    [Fact]
    public async Task Only_the_active_points_of_this_store_are_offered()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await AddPickupPointAsync(furniture, "Main Shop");
        await AddPickupPointAsync(furniture, "Closed Shop", isActive: false);
        await AddCollectionMethodAsync(furniture);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var otherShopper = new StorefrontApi(factory, furniture.OtherStore);

        var points = await shopper.GetJsonAsync<List<PickupPointView>>("/api/storefront/checkout/pickup-points/collection");
        using var otherStore = await otherShopper.GetAsync("/api/storefront/checkout/pickup-points/collection");

        Assert.Equal(["Main Shop"], points.Select(point => point.Name));
        Assert.Equal(HttpStatusCode.NotFound, otherStore.StatusCode);
    }

    [Fact]
    public async Task A_paid_order_is_shipped_with_a_tracking_number()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        var order = await PlaceOrderAsync(shopper);

        using var beforePayment = await ShipAsync(furniture, order.Number, "PKG-1");
        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        using var shipped = await ShipAsync(furniture, order.Number, "PKG-1");
        using var shippedAgain = await ShipAsync(furniture, order.Number, "PKG-2");
        var confirmation = await shopper.GetJsonAsync<OrderView>($"/api/storefront/orders/{order.Number}?token={order.Token}");

        Assert.Equal(HttpStatusCode.Conflict, beforePayment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, shipped.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, shippedAgain.StatusCode);
        Assert.Equal("Shipped", confirmation.Status);
        Assert.Equal(("Courier", "PKG-1"), (confirmation.Shipment!.Carrier, confirmation.Shipment.TrackingNumber));
    }

    [Fact]
    public async Task A_shipment_the_store_hands_over_needs_a_tracking_number()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        await AddToCartAsync(shopper, furniture.Products["oak-chair"], 1);
        var order = await PlaceOrderAsync(shopper);
        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);

        using var response = await ShipAsync(furniture, order.Number, trackingNumber: null);

        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_pickup_point_of_another_store_cannot_be_used()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await AddPickupPointAsync(furniture, "Main Shop");
        await AddCollectionMethodAsync(furniture);
        await AddCollectionMethodAsync(furniture, furniture.OtherStore.StoreId);
        var otherProduct = await furniture.Admin.CreateProductAsync();
        var otherListing = await furniture.Admin.ListProductAsync(furniture.OtherStore.StoreId, otherProduct, "Lamp", 10m);
        await furniture.Admin.StockAsync(otherProduct, 5);
        using var shopper = new StorefrontApi(factory, furniture.OtherStore);
        await AddToCartAsync(shopper, otherListing, 1);

        using var response = await shopper.PostAsync(
            "/api/storefront/checkout",
            Checkout.Request(shipping: "collection", pickupPoint: "main-shop"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> ShipAsync(FurnitureStore furniture, string number, string? trackingNumber) =>
        furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{number}/shipment",
            new { TrackingNumber = trackingNumber },
            CancellationToken);

    private async Task AddPickupPointAsync(FurnitureStore furniture, string name, bool isActive = true)
    {
        using var response = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/pickup-points",
            new { Name = name, Line1 = "4 Market Square", City = "Galway", PostalCode = "H91 AB12", Country = "IE", IsActive = isActive },
            CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task AddCollectionMethodAsync(FurnitureStore furniture, Guid? storeId = null)
    {
        using var response = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{storeId ?? furniture.Store.StoreId}/shipping-methods",
            new { Name = "Collection", ProviderKey = "manual", Price = 0m, VatRate = 21m, IsActive = true, RequiresPickupPoint = true },
            CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task AddToCartAsync(StorefrontApi shopper, Guid storeProductId, int quantity)
    {
        using var response = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = storeProductId, Quantity = quantity });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<PlacedOrder> PlaceOrderAsync(StorefrontApi shopper, string? pickupPoint = null)
    {
        var request = pickupPoint is null
            ? Checkout.Request()
            : Checkout.Request(shipping: "collection", pickupPoint: pickupPoint);
        using var response = await shopper.PostAsync("/api/storefront/checkout", request);

        return await shopper.ReadAsync<PlacedOrder>(response, HttpStatusCode.Created);
    }

    private sealed record PickupPointView(string Code, string Name, string City);
}

using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Orders;

public sealed class CartTests(ShopForgeApiFactory factory)
{
    [Fact]
    public async Task Items_can_be_added_changed_and_removed()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chair = furniture.Products["oak-chair"];
        using var shopper = new StorefrontApi(factory, furniture.Store);

        using var added = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = chair, Quantity = 2 });
        var afterAdd = await shopper.ReadAsync<CartView>(added);
        using var addedAgain = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = chair });
        var afterSecondAdd = await shopper.ReadAsync<CartView>(addedAgain);
        using var changed = await shopper.PutAsync($"/api/storefront/cart/items/{chair}", new { Quantity = 5 });
        var afterChange = await shopper.ReadAsync<CartView>(changed);
        using var removed = await shopper.DeleteAsync($"/api/storefront/cart/items/{chair}");
        var afterRemove = await shopper.ReadAsync<CartView>(removed);

        Assert.Equal((2, 200m), (afterAdd.Count, afterAdd.ItemsTotal));
        Assert.Equal(3, afterSecondAdd.Count);
        Assert.Equal((5, 500m), (afterChange.Count, afterChange.ItemsTotal));
        Assert.Empty(afterRemove.Items);
    }

    [Fact]
    public async Task A_cart_cookie_from_one_store_is_useless_on_another()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var added = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"] });
        await shopper.ReadAsync<CartView>(added);
        var cookie = added.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Single(value => value.StartsWith("shopforge_cart=", StringComparison.Ordinal)).Split(';')[0]
            : throw new InvalidOperationException("No cart cookie was issued.");

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://{furniture.OtherStore.HostName}/api/storefront/cart");
        request.Headers.Add("Cookie", cookie);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var cart = await response.Content.ReadFromJsonAsync<CartView>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(cart!.Items);
    }

    [Fact]
    public async Task Products_that_stop_being_sold_leave_the_cart()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chair = furniture.Products["oak-chair"];
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var added = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = chair });
        await shopper.ReadAsync<CartView>(added);

        using var hidden = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{chair}",
            new { Name = "Oak Chair", Slug = "oak-chair", Price = 100m, VatRate = 21m, IsVisible = false, SortOrder = 0 },
            TestContext.Current.CancellationToken);
        var cart = await shopper.GetJsonAsync<CartView>("/api/storefront/cart");

        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        Assert.Empty(cart.Items);
        Assert.Equal(1, cart.RemovedLines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task Impossible_quantities_are_rejected(int quantity)
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        using var response = await shopper.PostAsync(
            "/api/storefront/cart/items",
            new { StoreProductId = furniture.Products["oak-chair"], Quantity = quantity });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Products_of_another_store_cannot_be_added()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.OtherStore);

        using var response = await shopper.PostAsync("/api/storefront/cart/items", new { StoreProductId = furniture.Products["oak-chair"] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

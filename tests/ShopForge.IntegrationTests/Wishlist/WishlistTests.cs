using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Wishlist;

public sealed class WishlistTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_customer_keeps_a_list_of_what_they_want()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await CustomerAsync(furniture);
        var chair = furniture.Products["oak-chair"];

        using var added = await shopper.PostAsync("/api/storefront/account/wishlist", new { StoreProductId = chair });
        using var addedAgain = await shopper.PostAsync("/api/storefront/account/wishlist", new { StoreProductId = chair });
        var list = await shopper.GetJsonAsync<List<WishlistView>>("/api/storefront/account/wishlist");
        using var removed = await shopper.DeleteAsync($"/api/storefront/account/wishlist/{chair}");
        var afterRemoval = await shopper.GetJsonAsync<List<WishlistView>>("/api/storefront/account/wishlist");

        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, addedAgain.StatusCode);
        Assert.Equal(("Oak Chair", 100m), (list.Single().Name, list.Single().Price));
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(afterRemoval);
    }

    [Fact]
    public async Task A_wishlist_needs_an_account()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var guest = new StorefrontApi(factory, furniture.Store);

        using var list = await guest.GetAsync("/api/storefront/account/wishlist");
        using var add = await guest.PostAsync("/api/storefront/account/wishlist", new { StoreProductId = furniture.Products["oak-chair"] });

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, add.StatusCode);
    }

    [Fact]
    public async Task One_customer_cannot_see_another_customer_s_list()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var first = await CustomerAsync(furniture);
        using var second = await CustomerAsync(furniture);
        await first.PostAsync("/api/storefront/account/wishlist", new { StoreProductId = furniture.Products["oak-chair"] });

        var mine = await first.GetJsonAsync<List<WishlistView>>("/api/storefront/account/wishlist");
        var theirs = await second.GetJsonAsync<List<WishlistView>>("/api/storefront/account/wishlist");

        Assert.Single(mine);
        Assert.Empty(theirs);
    }

    [Fact]
    public async Task A_product_that_stops_being_sold_drops_out_of_the_list()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await CustomerAsync(furniture);
        var chair = furniture.Products["walnut-chair"];
        await shopper.PostAsync("/api/storefront/account/wishlist", new { StoreProductId = chair });

        using var hidden = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{chair}",
            new { Name = "Walnut Chair", Slug = "walnut-chair", Price = 200m, VatRate = 21m, IsVisible = false, SortOrder = 0 },
            CancellationToken);
        var list = await shopper.GetJsonAsync<List<WishlistView>>("/api/storefront/account/wishlist");

        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        Assert.Empty(list);
    }

    [Fact]
    public async Task A_product_of_another_store_cannot_be_wished_for()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await CustomerAsync(furniture);
        var otherProduct = await furniture.Admin.CreateProductAsync();
        var otherListing = await furniture.Admin.ListProductAsync(furniture.OtherStore.StoreId, otherProduct, "Lamp", 10m);

        using var response = await shopper.PostAsync("/api/storefront/account/wishlist", new { StoreProductId = otherListing });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<StorefrontApi> CustomerAsync(FurnitureStore furniture)
    {
        var email = $"wisher-{Guid.NewGuid():N}@example.test";
        var shopper = new StorefrontApi(factory, furniture.Store);

        await shopper.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password = "Shop-forge-2026", FirstName = "Wish", LastName = "Lister", Phone = (string?)null });
        var token = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.NextLinkFor(email)),
            link => link is not null,
            CancellationToken);
        using var verified = await shopper.PostAsync("/api/storefront/account/verify", new { Token = token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        return shopper;
    }

    private sealed record WishlistView(Guid StoreProductId, string Name, string Slug, decimal Price, string? ImageUrl);
}

using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Reviews;

public sealed class ReviewTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_customer_who_bought_it_can_review_it_once()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await BuyerOfAsync(furniture, "oak-chair");

        using var written = await shopper.PostAsync("/api/storefront/products/oak-chair/reviews", new { Rating = 5, Text = "Sturdy and handsome." });
        using var again = await shopper.PostAsync("/api/storefront/products/oak-chair/reviews", new { Rating = 1, Text = "Changed my mind." });

        Assert.Equal(HttpStatusCode.Accepted, written.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_customer_who_did_not_buy_it_cannot_review_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await BuyerOfAsync(furniture, "oak-chair");

        using var response = await shopper.PostAsync("/api/storefront/products/walnut-chair/reviews", new { Rating = 5, Text = "Never seen it." });
        using var anonymous = await new StorefrontApi(factory, furniture.Store)
            .PostAsync("/api/storefront/products/oak-chair/reviews", new { Rating = 5, Text = "Not signed in." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task A_review_is_invisible_until_the_store_publishes_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await BuyerOfAsync(furniture, "oak-chair");
        using var written = await shopper.PostAsync("/api/storefront/products/oak-chair/reviews", new { Rating = 4, Text = "Good chair." });

        var beforeModeration = await shopper.GetJsonAsync<ReviewsView>("/api/storefront/products/oak-chair/reviews");
        var pending = await PendingAsync(furniture);
        using var published = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/reviews/{pending.Single().Id}/publish", null, CancellationToken);
        var afterModeration = await shopper.GetJsonAsync<ReviewsView>("/api/storefront/products/oak-chair/reviews");
        var product = await shopper.GetJsonAsync<ProductView>("/api/storefront/products/oak-chair");

        Assert.Equal(HttpStatusCode.Accepted, written.StatusCode);
        Assert.Empty(beforeModeration.Reviews);
        Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);
        Assert.Equal(4, afterModeration.Reviews.Single().Rating);
        Assert.Equal((4m, 1), (product.Rating, product.ReviewCount));
    }

    [Fact]
    public async Task A_rejected_review_counts_for_nothing_and_cannot_be_rewritten()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await BuyerOfAsync(furniture, "beech-stool");
        await shopper.PostAsync("/api/storefront/products/beech-stool/reviews", new { Rating = 1, Text = "Unfair review." });
        var pending = await PendingAsync(furniture);

        using var rejected = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/reviews/{pending.Single().Id}/reject", null, CancellationToken);
        using var rewritten = await shopper.PostAsync("/api/storefront/products/beech-stool/reviews", new { Rating = 5, Text = "Second attempt." });
        var product = await shopper.GetJsonAsync<ProductView>("/api/storefront/products/beech-stool");

        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rewritten.StatusCode);
        Assert.Equal((0m, 0), (product.Rating, product.ReviewCount));
    }

    [Fact]
    public async Task The_rating_on_the_listing_follows_the_published_reviews()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var first = await BuyerOfAsync(furniture, "oak-bench");
        using var second = await BuyerOfAsync(furniture, "oak-bench");
        await first.PostAsync("/api/storefront/products/oak-bench/reviews", new { Rating = 5, Text = "Excellent." });
        await second.PostAsync("/api/storefront/products/oak-bench/reviews", new { Rating = 2, Text = "Wobbly." });

        foreach (var review in await PendingAsync(furniture))
        {
            using var published = await furniture.Admin.PostAsync(
                $"/api/admin/stores/{furniture.Store.StoreId}/reviews/{review.Id}/publish", null, CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, published.StatusCode);
        }

        var product = await first.GetJsonAsync<ProductView>("/api/storefront/products/oak-bench");

        Assert.Equal((3.5m, 2), (product.Rating, product.ReviewCount));
    }

    [Fact]
    public async Task Products_can_be_sorted_by_what_customers_thought()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await BuyerOfAsync(furniture, "walnut-chair");
        await shopper.PostAsync("/api/storefront/products/walnut-chair/reviews", new { Rating = 5, Text = "The best one." });
        var pending = await PendingAsync(furniture);
        await furniture.Admin.PostAsync($"/api/admin/stores/{furniture.Store.StoreId}/reviews/{pending.Single().Id}/publish", null, CancellationToken);

        var page = await shopper.GetJsonAsync<ProductPageView>("/api/storefront/products?sort=-rating");
        using var unknownSort = await shopper.GetAsync("/api/storefront/products?sort=stars");

        // Reviewed products come first; the unreviewed ones keep their own order behind them.
        Assert.Equal("walnut-chair", page.Items[0].Slug);
        Assert.Equal((5m, 1), (page.Items[0].Rating, page.Items[0].ReviewCount));
        Assert.Equal(HttpStatusCode.BadRequest, unknownSort.StatusCode);
    }

    [Fact]
    public async Task Only_a_buyer_who_has_not_reviewed_yet_is_asked_for_one()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = await BuyerOfAsync(furniture, "beech-stool");
        using var passerby = new StorefrontApi(factory, furniture.Store);

        var invited = await shopper.GetJsonAsync<ReviewsView>("/api/storefront/products/beech-stool/reviews");
        var otherProduct = await shopper.GetJsonAsync<ReviewsView>("/api/storefront/products/oak-chair/reviews");
        var anonymous = await passerby.GetJsonAsync<ReviewsView>("/api/storefront/products/beech-stool/reviews");
        await shopper.PostAsync("/api/storefront/products/beech-stool/reviews", new { Rating = 5, Text = "Just right." });
        var afterWriting = await shopper.GetJsonAsync<ReviewsView>("/api/storefront/products/beech-stool/reviews");

        Assert.True(invited.CanWrite);
        Assert.False(otherProduct.CanWrite);
        Assert.False(anonymous.CanWrite);
        Assert.False(afterWriting.CanWrite);
    }

    private async Task<StorefrontApi> BuyerOfAsync(FurnitureStore furniture, string product)
    {
        var email = $"buyer-{Guid.NewGuid():N}@example.test";
        var shopper = new StorefrontApi(factory, furniture.Store);

        await shopper.PostAsync(
            "/api/storefront/account/register",
            new { Email = email, Password = "Shop-forge-2026", FirstName = "Rea", LastName = "Viewer", Phone = (string?)null });
        var token = await factory.EventuallyAsync(
            () => Task.FromResult(factory.Emails.NextLinkFor(email)),
            link => link is not null,
            CancellationToken);
        using var verified = await shopper.PostAsync("/api/storefront/account/verify", new { Token = token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);

        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items",
            new { StoreProductId = furniture.Products[product], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request(email: email));
        var order = await shopper.ReadAsync<PlacedOrder>(placed, HttpStatusCode.Created);
        using var paid = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/orders/{order.Number}/payment", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        return shopper;
    }

    private async Task<List<AdminReviewView>> PendingAsync(FurnitureStore furniture) =>
        (await furniture.Admin.GetFromJsonAsync<List<AdminReviewView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/reviews?status=pending", CancellationToken))!;

    private sealed record ReviewsView(bool CanWrite, List<ReviewView> Reviews);

    private sealed record ReviewView(string Author, int Rating, string Text, DateTimeOffset WrittenAt);

    private sealed record AdminReviewView(Guid Id, string ProductName, string Author, int Rating, string Status);

    private sealed record ProductView(string Slug, decimal Rating, int ReviewCount);

    private sealed record ProductPageView(List<ProductRowView> Items);

    private sealed record ProductRowView(string Slug, decimal Rating, int ReviewCount);
}

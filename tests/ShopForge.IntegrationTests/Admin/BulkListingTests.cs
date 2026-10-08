using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Shared.Admin;

namespace ShopForge.IntegrationTests.Admin;

// A hundred listings change visibility, price or category in one action (D-183).
public sealed class BulkListingTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Many_listings_are_hidden_in_one_action()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chairs = Ids(furniture, "oak-chair", "walnut-chair", "beech-stool");

        var result = await BulkAsync(furniture, "visibility", new { StoreProductIds = chairs, IsVisible = false });
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var catalogue = await shopper.GetJsonAsync<ProductPage>("/api/storefront/products");

        Assert.Equal(3, result.Asked);
        Assert.Equal(3, result.Changed);
        Assert.Equal(["oak-bench"], catalogue.Items.Select(item => item.Slug));
    }

    // What was already as asked is counted as asked and not as changed, so a screen can tell the difference
    // between "nothing happened" and "nothing needed to".
    [Fact]
    public async Task A_listing_already_as_asked_is_counted_but_not_changed()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chairs = Ids(furniture, "oak-chair", "walnut-chair");

        await BulkAsync(furniture, "visibility", new { StoreProductIds = chairs, IsVisible = false });
        var again = await BulkAsync(furniture, "visibility", new { StoreProductIds = chairs, IsVisible = false });

        Assert.Equal(2, again.Asked);
        Assert.Equal(0, again.Changed);
    }

    // A selection of many where a colleague has deleted one must not lose the rest.
    [Fact]
    public async Task An_id_the_store_does_not_have_is_counted_but_skipped()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chairs = Ids(furniture, "oak-chair").Append(Guid.NewGuid()).ToArray();

        var result = await BulkAsync(furniture, "visibility", new { StoreProductIds = chairs, IsVisible = false });

        Assert.Equal(2, result.Asked);
        Assert.Equal(1, result.Changed);
    }

    // And another store's listing is one this store does not have, which the tenancy filter decides.
    [Fact]
    public async Task Another_stores_listing_is_not_touched()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var neighbour = await FurnitureStore.CreateAsync(factory);
        var theirs = Ids(neighbour, "oak-chair");

        var result = await BulkAsync(furniture, "visibility", new { StoreProductIds = theirs, IsVisible = false });
        var stillShown = await ListingsAsync(neighbour);

        Assert.Equal(1, result.Asked);
        Assert.Equal(0, result.Changed);
        Assert.True(stillShown.Items.Single(listing => listing.Slug == "oak-chair").IsVisible);
    }

    [Fact]
    public async Task Many_prices_are_set_to_one_number()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chairs = Ids(furniture, "oak-chair", "walnut-chair");

        var result = await BulkAsync(furniture, "price", new { StoreProductIds = chairs, Set = 149.50m });
        var listings = await ListingsAsync(furniture);

        Assert.Equal(2, result.Changed);
        Assert.Equal(149.50m, listings.Items.Single(listing => listing.Slug == "oak-chair").Price);
        Assert.Equal(149.50m, listings.Items.Single(listing => listing.Slug == "walnut-chair").Price);
        Assert.Equal(50m, listings.Items.Single(listing => listing.Slug == "beech-stool").Price);
    }

    // Moving them all by a percentage is the thing a merchant actually does, and the money it lands on is
    // rounded to what will be charged.
    [Fact]
    public async Task Many_prices_move_by_a_percentage_and_land_on_real_money()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await SetPriceAsync(furniture, "oak-chair", 99.99m);
        var chairs = Ids(furniture, "oak-chair", "beech-stool");

        var result = await BulkAsync(furniture, "price", new { StoreProductIds = chairs, ByPercent = 5m });
        var listings = await ListingsAsync(furniture);

        Assert.Equal(2, result.Changed);
        Assert.Equal(104.99m, listings.Items.Single(listing => listing.Slug == "oak-chair").Price);
        Assert.Equal(52.50m, listings.Items.Single(listing => listing.Slug == "beech-stool").Price);
    }

    [Fact]
    public async Task Prices_can_be_brought_down_as_well_as_up()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chairs = Ids(furniture, "oak-chair");

        await BulkAsync(furniture, "price", new { StoreProductIds = chairs, ByPercent = -10m });
        var listings = await ListingsAsync(furniture);

        Assert.Equal(90m, listings.Items.Single(listing => listing.Slug == "oak-chair").Price);
    }

    [Fact]
    public async Task A_price_change_must_be_one_thing_or_the_other()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chairs = Ids(furniture, "oak-chair");

        using var neither = await PostAsync(furniture, "price", new { StoreProductIds = chairs });
        using var both = await PostAsync(furniture, "price", new { StoreProductIds = chairs, Set = 10m, ByPercent = 5m });
        using var negative = await PostAsync(furniture, "price", new { StoreProductIds = chairs, Set = -1m });
        using var tooDeep = await PostAsync(furniture, "price", new { StoreProductIds = chairs, Set = 10.005m });

        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooDeep.StatusCode);
    }

    // Taking more than everything off cannot leave a shop paying people to shop there.
    [Fact]
    public async Task A_percentage_cannot_take_more_than_everything_off()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chairs = Ids(furniture, "oak-chair");

        using var refused = await PostAsync(furniture, "price", new { StoreProductIds = chairs, ByPercent = -120m });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // Adding to a category does not take anything out of the ones it is already in.
    [Fact]
    public async Task Listings_are_added_to_a_category_without_leaving_the_ones_they_are_in()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var sale = await furniture.Admin.CreateCategoryAsync(furniture.Store.StoreId, "Sale");
        var chairs = Ids(furniture, "oak-chair", "walnut-chair");

        var result = await BulkAsync(furniture, "categories", new { StoreProductIds = chairs, AddCategoryIds = new[] { sale } });
        var listings = await ListingsAsync(furniture);
        var oak = listings.Items.Single(listing => listing.Slug == "oak-chair");

        Assert.Equal(2, result.Changed);
        Assert.Contains(sale, oak.CategoryIds);
        Assert.Contains(furniture.ChairsCategoryId, oak.CategoryIds);
    }

    [Fact]
    public async Task Listings_are_taken_out_of_a_category()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chairs = Ids(furniture, "oak-chair", "walnut-chair");

        var result = await BulkAsync(
            furniture, "categories", new { StoreProductIds = chairs, RemoveCategoryIds = new[] { furniture.ChairsCategoryId } });
        var listings = await ListingsAsync(furniture);

        Assert.Equal(2, result.Changed);
        Assert.Empty(listings.Items.Single(listing => listing.Slug == "oak-chair").CategoryIds);
    }

    [Fact]
    public async Task A_category_the_store_does_not_have_is_refused_rather_than_ignored()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chairs = Ids(furniture, "oak-chair");

        using var refused = await PostAsync(
            furniture, "categories", new { StoreProductIds = chairs, AddCategoryIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_category_action_has_to_name_a_category()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chairs = Ids(furniture, "oak-chair");

        using var refused = await PostAsync(furniture, "categories", new { StoreProductIds = chairs });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // Retiring a discontinued range, which is what archiving is for (D-180).
    [Fact]
    public async Task A_range_is_retired_and_brought_back_in_one_action_each()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chairs = Ids(furniture, "oak-chair", "walnut-chair", "beech-stool");

        var archived = await BulkAsync(furniture, "archive", new { StoreProductIds = chairs, Archived = true });
        var working = await ListingsAsync(furniture);
        var restored = await BulkAsync(furniture, "archive", new { StoreProductIds = chairs, Archived = false });
        var back = await ListingsAsync(furniture);

        Assert.Equal(3, archived.Changed);
        Assert.Equal(1, working.TotalCount);
        Assert.Equal(3, restored.Changed);
        Assert.Equal(4, back.TotalCount);
    }

    [Fact]
    public async Task An_action_has_to_name_a_listing_and_cannot_name_a_thousand()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var none = await PostAsync(furniture, "visibility", new { StoreProductIds = Array.Empty<Guid>(), IsVisible = false });
        using var toomany = await PostAsync(
            furniture, "visibility", new { StoreProductIds = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()), IsVisible = false });

        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toomany.StatusCode);
    }

    // One entry for one decision, not one per listing: a hundred rows saying the same thing bury the log.
    [Fact]
    public async Task A_bulk_action_is_written_down_once_with_its_count()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var chairs = Ids(furniture, "oak-chair", "walnut-chair", "beech-stool");

        await BulkAsync(furniture, "price", new { StoreProductIds = chairs, ByPercent = 5m });

        var recorded = await factory.EventuallyAsync(
            () => furniture.Admin.GetFromJsonAsync<List<AuditRow>>("/api/admin/audit", CancellationToken),
            entries => entries!.Any(entry => entry.Action == "catalog.listings.price"),
            CancellationToken);

        var entry = Assert.Single(recorded!, candidate => candidate.Action == "catalog.listings.price");
        Assert.Contains("3", entry.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_action_that_changed_nothing_is_not_written_down()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        await BulkAsync(furniture, "visibility", new { StoreProductIds = new[] { Guid.NewGuid() }, IsVisible = false });
        var recorded = await furniture.Admin.GetFromJsonAsync<List<AuditRow>>("/api/admin/audit", CancellationToken);

        Assert.DoesNotContain("catalog.listings.visibility", recorded!.Select(entry => entry.Action));
    }

    private static Guid[] Ids(FurnitureStore furniture, params string[] slugs) =>
        [.. slugs.Select(slug => furniture.Products[slug])];

    private async Task<BulkResult> BulkAsync(FurnitureStore furniture, string action, object body)
    {
        using var response = await PostAsync(furniture, action, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<BulkResult>(CancellationToken))!;
    }

    private Task<HttpResponseMessage> PostAsync(FurnitureStore furniture, string action, object body) =>
        furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/bulk/{action}", body, CancellationToken);

    private Task<AdminListResponse<ListingRow>> ListingsAsync(FurnitureStore furniture) =>
        furniture.Admin.AdminPageAsync<ListingRow>(
            $"/api/admin/stores/{furniture.Store.StoreId}/products?pageSize=200", CancellationToken);

    private async Task SetPriceAsync(FurnitureStore furniture, string slug, decimal price)
    {
        var listing = (await ListingsAsync(furniture)).Items.Single(row => row.Slug == slug);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{listing.Id}",
            new { listing.Name, listing.Slug, Description = (string?)null, Price = price, VatRate = 21m, IsVisible = true, listing.SortOrder },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private sealed record BulkResult(int Asked, int Changed);

    private sealed record ListingRow(Guid Id, string Name, string Slug, decimal Price, bool IsVisible, int SortOrder, List<Guid> CategoryIds);

    private sealed record ProductPage(List<PagedItem> Items, int TotalCount);

    private sealed record PagedItem(string Slug);

    private sealed record AuditRow(string Action, string Subject);
}

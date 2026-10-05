using System.Net;
using System.Net.Http.Json;

namespace ShopForge.IntegrationTests.Catalog;

// Renaming a page does not break the links somebody already wrote down (D-166).
public sealed class SlugHistoryTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_renamed_listing_answers_where_it_went()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await RenameListingAsync(furniture, "oak-chair", "oak-dining-chair");

        using var shopper = new Orders.StorefrontApi(factory, furniture.Store, followRedirects: false);
        using var old = await shopper.GetAsync("/api/storefront/products/oak-chair");
        using var moved = await shopper.GetAsync("/api/storefront/products/oak-dining-chair");

        Assert.Equal(HttpStatusCode.MovedPermanently, old.StatusCode);
        Assert.Equal("/api/storefront/products/oak-dining-chair", old.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
    }

    // The trail points at the row, not at the name it took next, so any number of renames lead to the newest.
    [Fact]
    public async Task Two_renames_lead_to_the_newest_name()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await RenameListingAsync(furniture, "oak-chair", "oak-dining-chair");
        await RenameListingAsync(furniture, "oak-dining-chair", "oak-kitchen-chair");

        using var shopper = new Orders.StorefrontApi(factory, furniture.Store, followRedirects: false);
        using var first = await shopper.GetAsync("/api/storefront/products/oak-chair");
        using var second = await shopper.GetAsync("/api/storefront/products/oak-dining-chair");

        Assert.Equal("/api/storefront/products/oak-kitchen-chair", first.Headers.Location?.ToString());
        Assert.Equal("/api/storefront/products/oak-kitchen-chair", second.Headers.Location?.ToString());
    }

    // A name given up and then taken by something else belongs to whatever answers to it now.
    [Fact]
    public async Task A_slug_back_in_use_wins_over_its_own_history()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await RenameListingAsync(furniture, "oak-chair", "oak-dining-chair");
        await RenameListingAsync(furniture, "beech-stool", "oak-chair");

        using var shopper = new Orders.StorefrontApi(factory, furniture.Store);
        var reused = await shopper.GetJsonAsync<DetailView>("/api/storefront/products/oak-chair");

        Assert.Equal("Beech Stool", reused.Name);
    }

    [Fact]
    public async Task A_renamed_category_answers_where_it_went()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var renamed = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories/{furniture.ChairsCategoryId}",
            new { Name = "Chairs", Slug = "seating", SortOrder = 0 },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        using var shopper = new Orders.StorefrontApi(factory, furniture.Store, followRedirects: false);
        using var old = await shopper.GetAsync("/api/storefront/products?category=chairs");

        Assert.Equal(HttpStatusCode.MovedPermanently, old.StatusCode);
        Assert.Equal("/api/storefront/products?category=seating", old.Headers.Location?.ToString());
    }

    // A trail belongs to the store that left it; another store's old name is simply unknown here.
    [Fact]
    public async Task One_stores_old_address_means_nothing_in_another()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await RenameListingAsync(furniture, "oak-chair", "oak-dining-chair");

        using var elsewhere = new Orders.StorefrontApi(factory, furniture.OtherStore);
        using var asked = await elsewhere.GetAsync("/api/storefront/products/oak-chair");

        Assert.Equal(HttpStatusCode.NotFound, asked.StatusCode);
    }

    // A file renaming in bulk is the easiest way to lose every link at once, so it leaves the same trail.
    [Fact]
    public async Task An_import_that_renames_leaves_the_same_trail()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var imported = await furniture.Admin.ImportAsync(
            furniture.Store.StoreId,
            ImportFiles.Workbook(
                ["sku", "name", "slug", "price", "vat"],
                [FurnitureStore.SkuOf("oak-chair"), "Oak Chair", "oak-armchair", 100m, 21m]));
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);

        using var shopper = new Orders.StorefrontApi(factory, furniture.Store, followRedirects: false);
        using var old = await shopper.GetAsync("/api/storefront/products/oak-chair");

        Assert.Equal(HttpStatusCode.MovedPermanently, old.StatusCode);
        Assert.Equal("/api/storefront/products/oak-armchair", old.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_name_nobody_has_ever_used_is_still_missing()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var shopper = new Orders.StorefrontApi(factory, furniture.Store);
        using var asked = await shopper.GetAsync("/api/storefront/products/never-sold-this");

        Assert.Equal(HttpStatusCode.NotFound, asked.StatusCode);
    }

    private async Task RenameListingAsync(FurnitureStore furniture, string from, string to)
    {
        var listings = await furniture.Admin.GetFromJsonAsync<List<ListingView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/products", CancellationToken);
        var listing = listings!.Single(candidate => candidate.Slug == from);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{listing.Id}",
            new { listing.Name, Slug = to, listing.Description, listing.Price, listing.VatRate, listing.IsVisible, listing.SortOrder },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private sealed record DetailView(string Slug, string Name);

    private sealed record ListingView(Guid Id, string Name, string Slug, string? Description, decimal Price, decimal VatRate, bool IsVisible, int SortOrder);
}

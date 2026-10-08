using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Shared.Admin;

namespace ShopForge.IntegrationTests.Admin;

// Retire what history depends on, delete what nothing does, and say which it is (D-180).
public sealed class ArchivingTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // One filter hides a retired listing everywhere at once: the shop, the search, the feeds, the sitemap.
    [Fact]
    public async Task An_archived_listing_leaves_the_shop_the_search_and_the_sitemap()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        await ArchiveListingAsync(furniture, "walnut-chair");

        var catalogue = await shopper.GetJsonAsync<ProductPage>("/api/storefront/products");
        var found = await shopper.GetJsonAsync<ProductPage>("/api/storefront/products?q=walnut");
        using var page = await shopper.GetAsync("/api/storefront/products/walnut-chair");
        var sitemap = await shopper.GetStringAsync("/api/storefront/sitemap.xml");

        Assert.DoesNotContain("walnut-chair", catalogue.Items.Select(item => item.Slug));
        Assert.Empty(found.Items);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.DoesNotContain("/p/walnut-chair", sitemap, StringComparison.Ordinal);
    }

    // Hiding and retiring are different things: a hidden listing is one a merchant is still working on.
    [Fact]
    public async Task An_archived_listing_leaves_the_merchants_list_where_a_hidden_one_stays()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        await HideAsync(furniture, "oak-chair");
        await ArchiveListingAsync(furniture, "walnut-chair");

        var working = await ListingsAsync(furniture, "");
        var retired = await ListingsAsync(furniture, "?archived=true");

        Assert.Contains("oak-chair", working.Items.Select(item => item.Slug));
        Assert.DoesNotContain("walnut-chair", working.Items.Select(item => item.Slug));
        Assert.Equal(3, working.TotalCount);
        Assert.Equal(["walnut-chair"], retired.Items.Select(item => item.Slug));
    }

    [Fact]
    public async Task A_restored_listing_comes_back_to_both()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await ArchiveListingAsync(furniture, "walnut-chair");

        await RestoreListingAsync(furniture, "walnut-chair");
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var found = await shopper.GetJsonAsync<ProductPage>("/api/storefront/products?q=walnut");
        var working = await ListingsAsync(furniture, "");

        Assert.Equal(["walnut-chair"], found.Items.Select(item => item.Slug));
        Assert.Contains("walnut-chair", working.Items.Select(item => item.Slug));
        Assert.Empty((await ListingsAsync(furniture, "?archived=true")).Items);
    }

    [Fact]
    public async Task Archiving_twice_is_the_same_as_archiving_once()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        await ArchiveListingAsync(furniture, "walnut-chair");
        await ArchiveListingAsync(furniture, "walnut-chair");

        // One row and one record of it: a second archiving changed nothing, so there is nothing to write down
        // about it. Without that, a merchant clicking twice gets two entries about one decision.
        var recorded = await factory.EventuallyAsync(
            () => furniture.Admin.GetFromJsonAsync<List<AuditRow>>("/api/admin/audit", CancellationToken),
            entries => entries!.Any(entry => entry.Action == "catalog.listing.archived"),
            CancellationToken);

        Assert.Single((await ListingsAsync(furniture, "?archived=true")).Items);
        Assert.Single(recorded!, entry => entry.Action == "catalog.listing.archived");
    }

    // What nothing points at goes properly: a catalogue full of retired mistakes is its own kind of mess.
    [Fact]
    public async Task A_listing_nobody_has_bought_can_be_deleted()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var deleted = await DeleteListingAsync(furniture, "walnut-chair");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(3, (await ListingsAsync(furniture, "")).TotalCount);
        Assert.Empty((await ListingsAsync(furniture, "?archived=true")).Items);
    }

    // An order names the listing it sold, so the row does not vanish from under the paperwork — and the
    // refusal says so rather than being a bare no.
    [Fact]
    public async Task A_listing_somebody_has_bought_cannot_be_deleted_and_the_reason_says_why()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await BuyAsync(furniture, "walnut-chair");

        using var refused = await DeleteListingAsync(furniture, "walnut-chair");
        var problem = await refused.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("has been ordered", problem, StringComparison.Ordinal);
        Assert.Contains("Archive it instead", problem, StringComparison.Ordinal);
    }

    // And the thing it suggests actually works on the same row.
    [Fact]
    public async Task What_cannot_be_deleted_can_be_archived()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await BuyAsync(furniture, "walnut-chair");

        using var refused = await DeleteListingAsync(furniture, "walnut-chair");
        await ArchiveListingAsync(furniture, "walnut-chair");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(["walnut-chair"], (await ListingsAsync(furniture, "?archived=true")).Items.Select(item => item.Slug));
    }

    // A renamed listing leaves a trail pointing at the row (D-166), and a trail to nothing is worse than none.
    [Fact]
    public async Task A_listing_an_old_address_still_points_at_cannot_be_deleted()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await ReslugAsync(furniture, "walnut-chair", "walnut-dining-chair");

        // The fixture still knows the row by the name it was created with; the trail is what the rename left.
        using var refused = await DeleteListingAsync(furniture, "walnut-chair");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("used to answer to", await refused.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_archived_category_leaves_the_shop_and_the_merchants_list()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        using var archived = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories/{furniture.ChairsCategoryId}/archive", null, CancellationToken);

        var offered = await shopper.GetJsonAsync<List<StorefrontCategory>>("/api/storefront/categories");
        var working = await CategoriesAsync(furniture, "");
        var retired = await CategoriesAsync(furniture, "?archived=true");

        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        Assert.DoesNotContain("chairs", offered.Select(category => category.Slug));
        Assert.Empty(working);
        Assert.Equal(["chairs"], retired.Select(category => category.Slug));
    }

    // A category with listings in it is not a category to delete, and the reason names what to do about it.
    [Fact]
    public async Task A_category_with_listings_in_it_cannot_be_deleted()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var refused = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories/{furniture.ChairsCategoryId}", CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("listings are in it", await refused.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_category_can_be_deleted()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var spare = await furniture.Admin.CreateCategoryAsync(furniture.Store.StoreId, "Spare");

        using var deleted = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories/{spare}", CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.DoesNotContain("Spare", (await CategoriesAsync(furniture, "")).Select(category => category.Name));
    }

    // 28f refuses to delete a category with children at the database; this says so with a sentence first.
    [Fact]
    public async Task A_category_with_another_beneath_it_cannot_be_deleted()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var top = await furniture.Admin.CreateCategoryAsync(furniture.Store.StoreId, "Furniture");
        await furniture.Admin.CreateCategoryAsync(furniture.Store.StoreId, "Stools", top);

        using var refused = await furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories/{top}", CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("sits beneath it", await refused.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    // Retiring is a change worth recording, which is why Stage 17 is a dependency of this one.
    [Fact]
    public async Task Archiving_and_deleting_are_written_down()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await ArchiveListingAsync(furniture, "walnut-chair");
        await RestoreListingAsync(furniture, "walnut-chair");
        using var deleted = await DeleteListingAsync(furniture, "beech-stool");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var recorded = await factory.EventuallyAsync(
            () => furniture.Admin.GetFromJsonAsync<List<AuditRow>>(
                "/api/admin/audit", CancellationToken),
            entries => entries!.Count(entry => entry.Action.StartsWith("catalog.listing.", StringComparison.Ordinal)) >= 3,
            CancellationToken);

        var actions = recorded!.Select(entry => entry.Action).ToList();

        Assert.Contains("catalog.listing.archived", actions);
        Assert.Contains("catalog.listing.restored", actions);
        Assert.Contains("catalog.listing.deleted", actions);
    }

    private async Task ArchiveListingAsync(FurnitureStore furniture, string slug)
    {
        using var response = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{furniture.Products[slug]}/archive", null, CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task RestoreListingAsync(FurnitureStore furniture, string slug)
    {
        using var response = await furniture.Admin.PostAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{furniture.Products[slug]}/restore", null, CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private Task<HttpResponseMessage> DeleteListingAsync(FurnitureStore furniture, string slug) =>
        furniture.Admin.DeleteAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{furniture.Products[slug]}", CancellationToken);

    private Task<AdminListResponse<ListingRow>> ListingsAsync(FurnitureStore furniture, string query) =>
        furniture.Admin.AdminPageAsync<ListingRow>(
            $"/api/admin/stores/{furniture.Store.StoreId}/products{query}", CancellationToken);

    private async Task<List<CategoryRow>> CategoriesAsync(FurnitureStore furniture, string query) =>
        (await furniture.Admin.GetFromJsonAsync<List<CategoryRow>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/categories{query}", CancellationToken))!;

    private async Task BuyAsync(FurnitureStore furniture, string slug)
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);
        using var added = await shopper.PostAsync(
            "/api/storefront/cart/items", new { StoreProductId = furniture.Products[slug], Quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        using var placed = await shopper.PostAsync("/api/storefront/checkout", Checkout.Request());
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
    }

    private async Task HideAsync(FurnitureStore furniture, string slug) => await SaveAsync(furniture, slug, visible: false);

    private async Task ReslugAsync(FurnitureStore furniture, string slug, string to) =>
        await SaveAsync(furniture, slug, visible: true, slugTo: to);

    private async Task SaveAsync(FurnitureStore furniture, string slug, bool visible, string? slugTo = null)
    {
        var listing = (await ListingsAsync(furniture, "?pageSize=200")).Items.Single(row => row.Slug == slug);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{listing.Id}",
            new { listing.Name, Slug = slugTo ?? listing.Slug, Description = (string?)null, listing.Price, VatRate = 21m, IsVisible = visible, listing.SortOrder },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private sealed record ListingRow(Guid Id, string Name, string Slug, decimal Price, int SortOrder);

    private sealed record CategoryRow(Guid Id, string Name, string Slug);

    private sealed record StorefrontCategory(string Name, string Slug);

    private sealed record ProductPage(List<PagedItem> Items, int TotalCount);

    private sealed record PagedItem(string Slug);

    private sealed record AuditRow(string Action, string Subject);
}

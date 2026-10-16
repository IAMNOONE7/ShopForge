using System.Net;
using System.Net.Http.Json;
using ShopForge.IntegrationTests.Catalog;

namespace ShopForge.IntegrationTests.Admin;

// One listing by its id: what the editor reloads, or a link opens, without paging through the list for it (D-204).
public sealed class ListingReadTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_listing_is_read_by_its_id_as_it_was_saved()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var reduced = await furniture.Admin.PostAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/tax-classes", new { Name = "Reduced", Code = "reduced" }, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, reduced.StatusCode);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            ListingPath(furniture, "oak-chair"),
            new
            {
                Name = "Oak Chair",
                Slug = "oak-chair",
                Description = "Solid oak.",
                Price = 120m,
                VatRate = 21m,
                IsVisible = false,
                SortOrder = 7,
                Seo = new { Title = "The oak chair", Description = "Sit on oak.", SocialImageUrl = (string?)null, NoIndex = true },
                TaxClass = "reduced",
            },
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var listing = await furniture.Admin.GetFromJsonAsync<ListingView>(ListingPath(furniture, "oak-chair"), CancellationToken);

        Assert.Equivalent(
            new ListingView(
                furniture.Products["oak-chair"],
                furniture.ProductIds["oak-chair"],
                FurnitureStore.SkuOf("oak-chair"),
                "Oak Chair",
                "oak-chair",
                "Solid oak.",
                120m,
                false,
                7,
                [furniture.ChairsCategoryId],
                "reduced",
                "The oak chair",
                "Sit on oak.",
                true,
                null),
            listing,
            strict: true);
    }

    // A link to a retired listing is still a link to something real, so it is answered, and says when it was retired.
    [Fact]
    public async Task A_retired_listing_is_still_read_by_its_id_and_says_when_it_was_retired()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var archived = await furniture.Admin.PostAsync($"{ListingPath(furniture, "walnut-chair")}/archive", null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);

        var retired = await furniture.Admin.GetFromJsonAsync<ListingView>(ListingPath(furniture, "walnut-chair"), CancellationToken);
        var inUse = await furniture.Admin.GetFromJsonAsync<ListingView>(ListingPath(furniture, "oak-chair"), CancellationToken);
        var list = await furniture.Admin.AdminPageAsync<ListingView>(
            $"/api/admin/stores/{furniture.Store.StoreId}/products?archived=true", CancellationToken);

        Assert.Equal("walnut-chair", retired!.Slug);
        Assert.NotNull(retired.ArchivedAt);
        Assert.Equal(retired.ArchivedAt, Assert.Single(list.Items).ArchivedAt);
        Assert.Null(inUse!.ArchivedAt);
    }

    // The other store belongs to the same company and the same people run it, and still does not hold this listing.
    [Fact]
    public async Task A_listing_is_found_only_in_its_own_store()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var otherStore = await furniture.Admin.GetAsync(
            $"/api/admin/stores/{furniture.OtherStore.StoreId}/products/{furniture.Products["oak-chair"]}", CancellationToken);
        using var unknown = await furniture.Admin.GetAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{Guid.NewGuid()}", CancellationToken);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (otherStore.StatusCode, unknown.StatusCode));
    }

    private static string ListingPath(FurnitureStore furniture, string slug) =>
        $"/api/admin/stores/{furniture.Store.StoreId}/products/{furniture.Products[slug]}";

    private sealed record ListingView(
        Guid Id,
        Guid ProductId,
        string Sku,
        string Name,
        string Slug,
        string? Description,
        decimal Price,
        bool IsVisible,
        int SortOrder,
        List<Guid> CategoryIds,
        string? TaxClass,
        string? SeoTitle,
        string? SeoDescription,
        bool SeoNoIndex,
        DateTimeOffset? ArchivedAt);
}

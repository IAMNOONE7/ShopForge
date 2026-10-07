using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Orders;
using ShopForge.Stores.Domain;

namespace ShopForge.IntegrationTests.Catalog;

// One product, and one list, under one address rather than eleven (D-174).
public sealed class CanonicalAddressTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_product_has_one_address_whichever_category_it_was_reached_through()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        var product = await shopper.GetJsonAsync<ProductDetail>("/api/storefront/products/oak-chair");

        Assert.Equal($"https://{furniture.Store.HostName}/p/oak-chair", product.Seo.Canonical);
    }

    [Fact]
    public async Task The_front_page_and_a_category_each_name_themselves()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var front = await ListAsync(furniture, "");
        var chairs = await ListAsync(furniture, "?category=chairs");

        Assert.Equal($"https://{furniture.Store.HostName}/", front.Seo.Canonical);
        Assert.Equal($"https://{furniture.Store.HostName}/c/chairs", chairs.Seo.Canonical);
    }

    [Fact]
    public async Task A_later_page_of_a_list_names_itself_with_only_the_page_number()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var second = await ListAsync(furniture, "?category=chairs&pageSize=1&page=2");

        Assert.Equal($"https://{furniture.Store.HostName}/c/chairs?page=2", second.Seo.Canonical);
    }

    // The whole point: a filtered or sorted view is the same products chosen or ordered differently, and it
    // points at the list rather than becoming an address of its own.
    [Fact]
    public async Task A_filtered_or_sorted_list_points_at_the_list_itself()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var clean = $"https://{furniture.Store.HostName}/c/chairs";

        // Each carries a page number, because without one the clean address is the answer either way and the
        // assertion would pass on a rule that had stopped looking at the filter or the sort at all.
        var filtered = await ListAsync(furniture, "?category=chairs&f.material=oak&page=2&pageSize=1");
        var sorted = await ListAsync(furniture, "?category=chairs&sort=price&page=2&pageSize=1");
        var both = await ListAsync(furniture, "?category=chairs&sort=-price&f.material=oak&page=2&pageSize=1");
        var pagedButOtherwiseUntouched = await ListAsync(furniture, "?category=chairs&page=2&pageSize=1");

        Assert.Equal(clean, filtered.Seo.Canonical);
        Assert.Equal(clean, sorted.Seo.Canonical);
        Assert.Equal(clean, both.Seo.Canonical);
        Assert.Equal($"{clean}?page=2", pagedButOtherwiseUntouched.Seo.Canonical);
    }

    [Fact]
    public async Task A_reader_can_walk_the_pages_of_a_list_and_runs_out_at_each_end()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var clean = $"https://{furniture.Store.HostName}/c/chairs";

        var first = await ListAsync(furniture, "?category=chairs&pageSize=1");
        var middle = await ListAsync(furniture, "?category=chairs&pageSize=1&page=2");
        var last = await ListAsync(furniture, "?category=chairs&pageSize=1&page=3");

        Assert.Null(first.PreviousPage);
        Assert.Equal($"{clean}?page=2", first.NextPage);
        Assert.Equal(clean, middle.PreviousPage);
        Assert.Equal($"{clean}?page=3", middle.NextPage);
        Assert.Equal($"{clean}?page=2", last.PreviousPage);
        Assert.Null(last.NextPage);
    }

    [Fact]
    public async Task A_narrowed_list_offers_no_way_to_walk_it()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        // Page two is asked for as well as page one: a rule that had stopped noticing the filter would offer a
        // link back from it, which page one alone cannot show.
        var first = await ListAsync(furniture, "?category=chairs&f.material=oak&pageSize=1");
        var second = await ListAsync(furniture, "?category=chairs&f.material=oak&pageSize=1&page=2");

        Assert.Null(first.PreviousPage);
        Assert.Null(first.NextPage);
        Assert.Null(second.PreviousPage);
        Assert.Null(second.NextPage);
    }

    // D-124 serves every proved domain and redirects none, so one shop on two domains is two copies of every
    // page. The canonical names the primary one, which is the whole mitigation until Stage 20b can redirect.
    [Fact]
    public async Task A_shop_on_two_domains_names_the_primary_one_from_either()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var alias = await SecondDomainAsync(furniture);
        using var viaAlias = new StorefrontApi(factory, furniture.Store with { HostName = alias });
        using var viaPrimary = new StorefrontApi(factory, furniture.Store);

        var fromAlias = await viaAlias.GetJsonAsync<ProductDetail>("/api/storefront/products/oak-chair");
        var fromPrimary = await viaPrimary.GetJsonAsync<ProductDetail>("/api/storefront/products/oak-chair");

        Assert.Equal($"https://{furniture.Store.HostName}/p/oak-chair", fromAlias.Seo.Canonical);
        Assert.Equal(fromPrimary.Seo.Canonical, fromAlias.Seo.Canonical);
    }

    // A crawler is told not to spend its time on a filtered list, in whichever query parameter the filter
    // turns up: a rule naming "?f." alone never matches "?page=2&f.material=oak".
    [Fact]
    public async Task A_filter_is_disallowed_in_either_query_position()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        var robots = await shopper.GetStringAsync("/api/storefront/robots.txt");

        Assert.Contains("Disallow: /*?f.", robots, StringComparison.Ordinal);
        Assert.Contains("Disallow: /*&f.", robots, StringComparison.Ordinal);
        Assert.Contains("Disallow: /*?sort=", robots, StringComparison.Ordinal);
        Assert.Contains("Disallow: /*&sort=", robots, StringComparison.Ordinal);
    }

    // A page's address is not a page's permission: the canonical says which address to index, and noindex
    // says not to index at all. A page carrying both would be two answers to one question.
    [Fact]
    public async Task A_page_that_hides_itself_still_names_its_address()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await HideAsync(furniture, "oak-chair");

        using var shopper = new StorefrontApi(factory, furniture.Store);
        var product = await shopper.GetJsonAsync<ProductDetail>("/api/storefront/products/oak-chair");

        Assert.True(product.Seo.NoIndex);
        Assert.Equal($"https://{furniture.Store.HostName}/p/oak-chair", product.Seo.Canonical);
    }

    private async Task<ProductPage> ListAsync(FurnitureStore furniture, string query)
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);

        return await shopper.GetJsonAsync<ProductPage>($"/api/storefront/products{query}");
    }

    private async Task HideAsync(FurnitureStore furniture, string slug)
    {
        var storeProductId = furniture.Products[slug];
        var listings = await furniture.Admin.AdminListAsync<ListingView>($"/api/admin/stores/{furniture.Store.StoreId}/products", CancellationToken);
        var listing = listings!.Single(candidate => candidate.Id == storeProductId);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{storeProductId}",
            new
            {
                listing.Name,
                listing.Slug,
                listing.Description,
                listing.Price,
                listing.VatRate,
                listing.IsVisible,
                listing.SortOrder,
                Seo = new { Title = (string?)null, Description = (string?)null, SocialImageUrl = (string?)null, NoIndex = true },
            },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task<string> SecondDomainAsync(FurnitureStore furniture)
    {
        var host = TestStores.UniqueHostName("alias");

        await using var scope = TestStores.CreateScope(factory.Services, furniture.Store);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var store = await dbContext.Set<Store>().Include(store => store.Domains)
            .SingleAsync(store => store.Id == furniture.Store.StoreId, CancellationToken);

        store.AddDomain(host, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(CancellationToken);

        return host;
    }

    private sealed record ListingView(Guid Id, string Name, string Slug, string? Description, decimal Price, decimal VatRate, bool IsVisible, int SortOrder);

    private sealed record ProductPage(PageSeoView Seo, string? PreviousPage, string? NextPage);

    private sealed record ProductDetail(string Slug, PageSeoView Seo);

    private sealed record PageSeoView(string Title, bool NoIndex, string? Canonical);
}

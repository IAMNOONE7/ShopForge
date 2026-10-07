using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Search;

// A shopper types what they want and finds it, with the filters, facets, sorting and paging of the page they
// are already on still working (D-178).
public sealed class CatalogSearchTests(ShopForgeApiFactory factory)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_product_is_found_by_its_name()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var found = await SearchAsync(furniture, "walnut");

        Assert.Equal(["walnut-chair"], found.Items.Select(item => item.Slug));
    }

    [Fact]
    public async Task A_product_is_found_by_words_in_its_description()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await DescribeAsync(furniture, "oak-chair", "A chair for a dining room, assembled by hand in Brno.");

        var found = await SearchAsync(furniture, "Brno");

        Assert.Equal(["oak-chair"], found.Items.Select(item => item.Slug));
    }

    // A code is not a word in any language, so it is indexed as typed and found as typed.
    [Fact]
    public async Task A_product_is_found_by_its_code()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var found = await SearchAsync(furniture, FurnitureStore.SkuOf("beech-stool"));

        Assert.Equal(["beech-stool"], found.Items.Select(item => item.Slug));
    }

    // Only the text attributes a shop marks searchable, and only once it has marked them.
    [Fact]
    public async Task Words_in_an_attribute_are_found_once_the_shop_says_they_are_searchable()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await SetCareAsync(furniture, "oak-chair", "Wipe with a damp cloth and beeswax");

        var before = await SearchAsync(furniture, "beeswax");
        await SearchableAsync(furniture, "Care", searchable: true);
        var after = await SearchAsync(furniture, "beeswax");

        Assert.Empty(before.Items);
        Assert.Equal(["oak-chair"], after.Items.Select(item => item.Slug));
    }

    [Fact]
    public async Task An_attribute_that_stops_being_searchable_stops_being_matched()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await SetCareAsync(furniture, "oak-chair", "Wipe with a damp cloth and beeswax");
        await SearchableAsync(furniture, "Care", searchable: true);

        await SearchableAsync(furniture, "Care", searchable: false);
        var found = await SearchAsync(furniture, "beeswax");

        Assert.Empty(found.Items);
    }

    // Only text has words worth matching: a number is found by filtering.
    [Fact]
    public async Task A_measurement_cannot_be_marked_searchable()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        using var refused = await SaveAttributeAsync(furniture, "Width", searchable: true);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // The point of folding it into the one query: everything narrows the same set.
    [Fact]
    public async Task A_search_and_a_filter_narrow_the_same_set()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await DescribeAsync(furniture, "oak-chair", "A sturdy chair.");
        await DescribeAsync(furniture, "walnut-chair", "A sturdy chair.");

        var all = await SearchAsync(furniture, "sturdy");
        var oak = await SearchAsync(furniture, "sturdy", "&f.material=oak");

        Assert.Equal(2, all.TotalCount);
        Assert.Equal(1, oak.TotalCount);
        Assert.Equal(["oak-chair"], oak.Items.Select(item => item.Slug));
    }

    [Fact]
    public async Task A_facet_count_on_a_search_agrees_with_the_results()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await DescribeAsync(furniture, "oak-chair", "A sturdy chair.");
        await DescribeAsync(furniture, "walnut-chair", "A sturdy chair.");

        var found = await SearchAsync(furniture, "sturdy", "&category=chairs");
        var oak = found.Filters.Single(filter => filter.Code == "material").Options!.Single(option => option.Code == "oak");
        var filtered = await SearchAsync(furniture, "sturdy", "&category=chairs&f.material=oak");

        Assert.Equal(oak.Count, filtered.TotalCount);
    }

    [Fact]
    public async Task Paging_and_sorting_work_over_what_was_found()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await DescribeAsync(furniture, "oak-chair", "A sturdy chair.");
        await DescribeAsync(furniture, "walnut-chair", "A sturdy chair.");
        await DescribeAsync(furniture, "beech-stool", "A sturdy stool.");

        var cheapestFirst = await SearchAsync(furniture, "sturdy", "&sort=price&pageSize=2");
        var second = await SearchAsync(furniture, "sturdy", "&sort=price&pageSize=2&page=2");

        Assert.Equal(3, cheapestFirst.TotalCount);
        Assert.Equal(["beech-stool", "oak-chair"], cheapestFirst.Items.Select(item => item.Slug));
        Assert.Equal(["walnut-chair"], second.Items.Select(item => item.Slug));
    }

    // A hit in a name counts for more than one in a description, which is what makes the first result the
    // right one when both match. The word is in one listing's name only and in the other's description only:
    // a code contains the words of its own name, so any word that is in both would be carried at the name's
    // weight by the code as well and prove nothing.
    [Fact]
    public async Task The_best_answer_comes_first_without_being_asked()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await RenameAsync(furniture, "walnut-chair", "Walnut Chesterfield");

        // And the one that merely mentions it is first in the shop's own order, so the tie-break cannot be
        // what puts the named one ahead.
        await DescribeAsync(furniture, "beech-stool", "Goes with our Chesterfield.", sortOrder: -5);

        var found = await SearchAsync(furniture, "chesterfield");

        Assert.Equal(["walnut-chair", "beech-stool"], found.Items.Select(item => item.Slug));
    }

    [Fact]
    public async Task Relevance_is_only_an_order_for_a_list_somebody_searched()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var shopper = new StorefrontApi(factory, furniture.Store);

        using var withoutTerms = await shopper.GetAsync("/api/storefront/products?sort=relevance");
        using var withTerms = await shopper.GetAsync("/api/storefront/products?q=chair&sort=relevance");

        Assert.Equal(HttpStatusCode.BadRequest, withoutTerms.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withTerms.StatusCode);
    }

    [Fact]
    public async Task A_hidden_listing_is_never_found()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        await HideAsync(furniture, "walnut-chair");

        var found = await SearchAsync(furniture, "walnut");

        Assert.Empty(found.Items);
    }

    // The tenancy filters do not reach a raw query, so the statement scopes itself and this says it works.
    [Fact]
    public async Task Nothing_crosses_a_store_boundary()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        var neighbour = await FurnitureStore.CreateAsync(factory);
        await DescribeAsync(neighbour, "oak-chair", "A chair with a rhubarb finish.");

        var ours = await SearchAsync(furniture, "rhubarb");
        var theirs = await SearchAsync(neighbour, "rhubarb");

        Assert.Empty(ours.Items);
        Assert.Equal(["oak-chair"], theirs.Items.Select(item => item.Slug));
    }

    [Fact]
    public async Task Punctuation_and_a_quoted_phrase_are_taken_as_typed_rather_than_refused()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var punctuated = await SearchAsync(furniture, "walnut!!! & <>");
        var phrase = await SearchAsync(furniture, "\"walnut chair\"");

        Assert.Equal(["walnut-chair"], punctuated.Items.Select(item => item.Slug));
        Assert.Equal(["walnut-chair"], phrase.Items.Select(item => item.Slug));
    }

    // A page with no terms and a page with empty terms are the same page: the whole catalogue.
    [Fact]
    public async Task Nothing_typed_is_not_a_search()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        var empty = await SearchAsync(furniture, "");
        var oneLetter = await SearchAsync(furniture, "a");
        var everything = await SearchAsync(furniture, null);

        Assert.Equal(everything.TotalCount, empty.TotalCount);
        Assert.Equal(everything.TotalCount, oneLetter.TotalCount);
        Assert.Equal(4, everything.TotalCount);
    }

    // What Stage 33 will read: the term and whether the shop had an answer, and nothing about who asked.
    [Fact]
    public async Task What_shoppers_look_for_is_recorded_with_whether_it_was_found()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);

        await SearchAsync(furniture, "walnut");
        await SearchAsync(furniture, "hammock");

        var asked = await factory.QueryAsync(furniture.Store, async dbContext => await dbContext.Database
            .SqlQuery<LoggedSearch>($"SELECT terms AS \"terms\", found AS \"found\" FROM catalog.search_queries WHERE store_id = {furniture.Store.StoreId}")
            .ToListAsync(CancellationToken));

        Assert.Equal(1, asked.Single(query => query.Terms == "walnut").Found);
        Assert.Equal(0, asked.Single(query => query.Terms == "hammock").Found);
    }

    private async Task<ProductPage> SearchAsync(FurnitureStore furniture, string? terms, string extra = "")
    {
        using var shopper = new StorefrontApi(factory, furniture.Store);
        var query = terms is null ? extra : $"q={Uri.EscapeDataString(terms)}{extra}";

        return await shopper.GetJsonAsync<ProductPage>($"/api/storefront/products?{query.TrimStart('&')}");
    }

    private async Task DescribeAsync(FurnitureStore furniture, string slug, string description, int? sortOrder = null)
    {
        var listing = await ListingAsync(furniture, slug);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{listing.Id}",
            new
            {
                listing.Name,
                listing.Slug,
                Description = description,
                listing.Price,
                listing.VatRate,
                listing.IsVisible,
                SortOrder = sortOrder ?? listing.SortOrder,
            },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    // The name only: the slug and therefore the code stay as they were.
    private async Task RenameAsync(FurnitureStore furniture, string slug, string name)
    {
        var listing = await ListingAsync(furniture, slug);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{listing.Id}",
            new { Name = name, listing.Slug, listing.Description, listing.Price, listing.VatRate, listing.IsVisible, listing.SortOrder },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task HideAsync(FurnitureStore furniture, string slug)
    {
        var listing = await ListingAsync(furniture, slug);

        using var saved = await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/products/{listing.Id}",
            new { listing.Name, listing.Slug, listing.Description, listing.Price, listing.VatRate, IsVisible = false, listing.SortOrder },
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task<ListingView> ListingAsync(FurnitureStore furniture, string slug)
    {
        var listings = await furniture.Admin.AdminListAsync<ListingView>($"/api/admin/stores/{furniture.Store.StoreId}/products", CancellationToken);

        return listings!.Single(candidate => candidate.Slug == slug);
    }

    private async Task SetCareAsync(FurnitureStore furniture, string slug, string care)
    {
        using var saved = await furniture.Admin.SetAttributesAsync(
            furniture.Store.StoreId, furniture.Products[slug], new { care });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task SearchableAsync(FurnitureStore furniture, string name, bool searchable)
    {
        using var saved = await SaveAttributeAsync(furniture, name, searchable);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    private async Task<HttpResponseMessage> SaveAttributeAsync(FurnitureStore furniture, string name, bool searchable)
    {
        var attributes = await furniture.Admin.GetFromJsonAsync<List<AttributeView>>(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes", CancellationToken);
        var attribute = attributes!.Single(candidate => candidate.Name == name);

        return await furniture.Admin.PutAsJsonAsync(
            $"/api/admin/stores/{furniture.Store.StoreId}/attributes/{attribute.Id}",
            new
            {
                attribute.Name,
                attribute.Unit,
                attribute.IsFilterable,
                attribute.IsVisibleOnProductPage,
                attribute.SortOrder,
                attribute.IsInFeeds,
                IsSearchable = searchable,
            },
            CancellationToken);
    }

    private sealed record ProductPage(List<PagedItem> Items, int TotalCount, List<Facet> Filters);

    private sealed record PagedItem(string Slug, string Name, decimal Price);

    private sealed record Facet(string Code, List<FacetOption>? Options);

    private sealed record FacetOption(string Code, int Count);

    private sealed record ListingView(Guid Id, string Name, string Slug, string? Description, decimal Price, decimal VatRate, bool IsVisible, int SortOrder);

    private sealed record AttributeView(Guid Id, string Name, string? Unit, bool IsFilterable, bool IsVisibleOnProductPage, int SortOrder, bool IsInFeeds);

    private sealed record LoggedSearch(string Terms, int Found);
}

using ShopForge.Shared.Stores;

namespace ShopForge.UnitTests.Seo;

// Which of the addresses a list can be reached by is worth indexing, and how a reader walks it (D-174).
public sealed class PagedPagesTests
{
    private const string Chairs = "https://acme.example/c/chairs";

    [Fact]
    public void The_first_page_of_a_list_is_the_list_itself()
    {
        Assert.Equal(Chairs, PagedPages.At(Chairs, 1));
        Assert.Equal(Chairs, PagedPages.Canonical(Chairs, 1, narrowedOrReordered: false));
    }

    [Fact]
    public void A_later_page_says_which_page_and_nothing_else()
    {
        Assert.Equal($"{Chairs}?page=4", PagedPages.At(Chairs, 4));
        Assert.Equal($"{Chairs}?page=4", PagedPages.Canonical(Chairs, 4, narrowedOrReordered: false));
    }

    // The point of the slice: one product, and one list, under one address rather than eleven.
    [Fact]
    public void A_narrowed_or_reordered_list_points_at_the_list_itself()
    {
        Assert.Equal(Chairs, PagedPages.Canonical(Chairs, 1, narrowedOrReordered: true));
        Assert.Equal(Chairs, PagedPages.Canonical(Chairs, 7, narrowedOrReordered: true));
    }

    [Fact]
    public void The_first_page_has_nothing_before_it()
    {
        Assert.Null(PagedPages.Previous(Chairs, 1, narrowedOrReordered: false));
        Assert.Equal(Chairs, PagedPages.Previous(Chairs, 2, narrowedOrReordered: false));
        Assert.Equal($"{Chairs}?page=2", PagedPages.Previous(Chairs, 3, narrowedOrReordered: false));
    }

    [Fact]
    public void The_last_page_has_nothing_after_it()
    {
        Assert.Equal($"{Chairs}?page=2", PagedPages.Next(Chairs, 1, pageSize: 10, totalCount: 25, narrowedOrReordered: false));
        Assert.Equal($"{Chairs}?page=3", PagedPages.Next(Chairs, 2, pageSize: 10, totalCount: 25, narrowedOrReordered: false));
        Assert.Null(PagedPages.Next(Chairs, 3, pageSize: 10, totalCount: 25, narrowedOrReordered: false));
    }

    // The boundary the comparison turns on: a list that ends exactly where a page ends has no next page, and
    // offering one sends a crawler to an empty address.
    [Fact]
    public void A_list_that_ends_exactly_where_a_page_ends_has_nothing_after_it()
    {
        Assert.Null(PagedPages.Next(Chairs, 2, pageSize: 10, totalCount: 20, narrowedOrReordered: false));
        Assert.Equal($"{Chairs}?page=2", PagedPages.Next(Chairs, 1, pageSize: 10, totalCount: 20, narrowedOrReordered: false));
        Assert.Null(PagedPages.Next(Chairs, 1, pageSize: 10, totalCount: 10, narrowedOrReordered: false));
    }

    [Fact]
    public void A_list_that_fits_on_one_page_has_neither()
    {
        Assert.Null(PagedPages.Previous(Chairs, 1, narrowedOrReordered: false));
        Assert.Null(PagedPages.Next(Chairs, 1, pageSize: 24, totalCount: 4, narrowedOrReordered: false));
    }

    [Fact]
    public void An_empty_list_has_nothing_after_it()
    {
        Assert.Null(PagedPages.Next(Chairs, 1, pageSize: 24, totalCount: 0, narrowedOrReordered: false));
    }

    // A view that canonicalises elsewhere is not a sequence worth walking: a prev/next pointing into it would
    // disagree with the canonical, which is two answers to one question.
    [Fact]
    public void A_narrowed_list_offers_no_way_to_walk_it()
    {
        Assert.Null(PagedPages.Previous(Chairs, 3, narrowedOrReordered: true));
        Assert.Null(PagedPages.Next(Chairs, 1, pageSize: 10, totalCount: 25, narrowedOrReordered: true));
    }

    // A catalogue past two billion products would overflow a plain multiplication before this question is
    // even asked, and the answer would be "no next page" on the last one that matters.
    [Fact]
    public void A_very_large_catalogue_still_knows_whether_there_is_another_page()
    {
        Assert.Equal($"{Chairs}?page=3", PagedPages.Next(Chairs, 2, pageSize: 1_000_000_000, totalCount: int.MaxValue, narrowedOrReordered: false));
        Assert.Null(PagedPages.Next(Chairs, 3, pageSize: 1_000_000_000, totalCount: int.MaxValue, narrowedOrReordered: false));
    }

    [Fact]
    public void The_resolver_carries_the_canonical_through_with_the_rest_of_the_metadata()
    {
        var seo = PageMetadata.For(StoreSeo.None, "Chairs", PageSeoOverrides.None, $"{Chairs}?page=2");

        Assert.Equal($"{Chairs}?page=2", seo.Canonical);
        Assert.Equal("Chairs", seo.Title);
    }

    // A shop with no proved domain has no absolute address, and a relative canonical leaves open exactly the
    // question the field exists to answer (D-164).
    [Fact]
    public void A_shop_with_no_address_states_no_canonical()
    {
        Assert.Null(PageMetadata.For(StoreSeo.None, "Chairs", PageSeoOverrides.None).Canonical);
    }
}

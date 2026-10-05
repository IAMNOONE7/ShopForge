using ShopForge.Shared.Stores;

namespace ShopForge.UnitTests.Stores;

// Three levels, each falling back to the one above it (D-165).
public sealed class PageMetadataTests
{
    private static readonly StoreSeo Store = new(" Acme Furniture ", "Chairs made in Brno.", "https://acme.test/social.png", NoIndex: false);

    [Fact]
    public void A_page_with_nothing_of_its_own_takes_the_stores_answer()
    {
        var page = PageMetadata.For(Store, "Oak Chair", PageSeoOverrides.None);

        Assert.Equal("Oak Chair — Acme Furniture", page.Title);
        Assert.Equal("Chairs made in Brno.", page.Description);
        Assert.Equal("https://acme.test/social.png", page.SocialImageUrl);
        Assert.False(page.NoIndex);
    }

    // A merchant who writes a title means that exact title, suffix and all.
    [Fact]
    public void A_title_somebody_wrote_is_used_as_written()
    {
        var page = PageMetadata.For(Store, "Oak Chair", new PageSeoOverrides("Buy an oak chair today", null, null, NoIndex: false));

        Assert.Equal("Buy an oak chair today", page.Title);
    }

    [Fact]
    public void A_store_with_no_suffix_leaves_the_page_name_alone()
    {
        var page = PageMetadata.For(StoreSeo.None, "Oak Chair", PageSeoOverrides.None);

        Assert.Equal("Oak Chair", page.Title);
        Assert.Null(page.Description);
        Assert.Null(page.SocialImageUrl);
    }

    [Fact]
    public void A_page_overrides_the_description_and_the_image_without_touching_the_title()
    {
        var page = PageMetadata.For(Store, "Oak Chair", new PageSeoOverrides(null, "One chair, many years.", "https://acme.test/chair.png", NoIndex: false));

        Assert.Equal("Oak Chair — Acme Furniture", page.Title);
        Assert.Equal("One chair, many years.", page.Description);
        Assert.Equal("https://acme.test/chair.png", page.SocialImageUrl);
    }

    // A shop that is not ready to be found hides all of itself, and a page cannot argue its way back in.
    [Fact]
    public void A_store_that_hides_itself_hides_every_page_of_itself()
    {
        var hidden = Store with { NoIndex = true };

        Assert.True(PageMetadata.For(hidden, "Oak Chair", PageSeoOverrides.None).NoIndex);
        Assert.True(PageMetadata.For(hidden, "Oak Chair", new PageSeoOverrides(null, null, null, NoIndex: false)).NoIndex);
    }

    [Fact]
    public void A_page_can_hide_itself_in_a_shop_that_is_otherwise_open()
    {
        var page = PageMetadata.For(Store, "Oak Chair", new PageSeoOverrides(null, null, null, NoIndex: true));

        Assert.True(page.NoIndex);
    }

    // Somebody who clears a field by selecting it and pressing space has cleared it.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_field_holding_nothing_but_space_counts_as_absent(string blank)
    {
        var page = PageMetadata.For(Store, "Oak Chair", new PageSeoOverrides(blank, blank, blank, NoIndex: false));

        Assert.Equal("Oak Chair — Acme Furniture", page.Title);
        Assert.Equal("Chairs made in Brno.", page.Description);
        Assert.Equal("https://acme.test/social.png", page.SocialImageUrl);
    }
}

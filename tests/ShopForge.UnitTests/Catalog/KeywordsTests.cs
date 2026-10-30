using ShopForge.Catalog.Domain;

namespace ShopForge.UnitTests.Catalog;

// The words a merchant adds for search, on a listing or a category (D-226).
public sealed class KeywordsTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  křížák, phillips ", "křížák, phillips")]
    [InlineData("sikovky;\nhasák", "sikovky;\nhasák")]
    public void Blank_is_none_and_the_rest_is_kept_as_typed(string? typed, string? kept) =>
        Assert.Equal(kept, Keywords.Tidied(typed));

    [Fact]
    public void Five_hundred_characters_is_the_most()
    {
        var most = new string('a', Keywords.MaxLength);

        Assert.Equal(most, Keywords.Tidied(" " + most + " "));
        Assert.True(Keywords.Fit(" " + most + " "));
        Assert.False(Keywords.Fit(most + "a"));
        Assert.Throws<ArgumentException>(() => Keywords.Tidied(most + "a"));
    }

    [Fact]
    public void A_listing_and_a_category_say_whether_their_keywords_changed()
    {
        var category = new Category(Guid.NewGuid(), "Kleště", "kleste", 0);

        Assert.True(category.SetSearchKeywords("kombinačky"));
        Assert.False(category.SetSearchKeywords(" kombinačky "));
        Assert.True(category.SetSearchKeywords(""));
        Assert.Null(category.SearchKeywords);
    }

    [Fact]
    public void A_category_s_search_text_says_whether_it_changed()
    {
        var category = new Category(Guid.NewGuid(), "Kleště", "kleste", 0);

        Assert.True(category.SetSearchText("Kleště Ruční nářadí"));
        Assert.False(category.SetSearchText("Kleště Ruční nářadí"));
        Assert.Equal("Kleště Ruční nářadí", category.SearchText);
    }

    // Text has words, and so do the options a listing holds; a number, a yes or no and a date are filtered.
    [Theory]
    [InlineData("Text", true)]
    [InlineData("Select", true)]
    [InlineData("MultiSelect", true)]
    [InlineData("Integer", false)]
    [InlineData("Decimal", false)]
    [InlineData("Boolean", false)]
    [InlineData("Date", false)]
    public void Only_text_and_options_can_be_searched(string type, bool searchable)
    {
        var definition = new AttributeDefinition(
            Guid.NewGuid(), "attribute", "Attribute", Enum.Parse<AttributeType>(type), new AttributeSettings(Unit: null, IsFilterable: false, IsVisibleOnProductPage: true, SortOrder: 0));
        var settings = new AttributeSettings(Unit: null, IsFilterable: false, IsVisibleOnProductPage: true, SortOrder: 0, IsSearchable: true);

        Assert.Equal(searchable, definition.CanBeSearched);

        if (searchable)
        {
            definition.Update("Attribute", settings);
            Assert.True(definition.IsSearchable);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => definition.Update("Attribute", settings));
        }
    }
}

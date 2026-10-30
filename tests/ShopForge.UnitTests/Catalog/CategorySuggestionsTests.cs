using ShopForge.Catalog.Categories;
using ShopForge.Catalog.Development;
using ShopForge.Catalog.Search;

namespace ShopForge.UnitTests.Catalog;

// The categories the search box offers, matched against the store's list in memory (D-227): every word typed begins a
// word of the name or the keywords, and the category named best comes first.
public sealed class CategorySuggestionsTests
{
    private static readonly StoreCategoryTree ToolShop =
        TreeOf([.. ToolShopCatalogue.Categories.Select(category => (category.Slug, category.Name, category.ParentSlug, category.Keywords))]);

    [Theory]
    [InlineData("šr", "sroubovaky srouby")]
    [InlineData("sr", "sroubovaky srouby")]
    [InlineData("ŠR", "sroubovaky srouby")]
    [InlineData("šroubováky", "sroubovaky")]
    [InlineData("SROUBOVAKY", "sroubovaky")]
    [InlineData("šroubovák", "sroubovaky")]
    [InlineData("šrouby", "srouby sroubovaky")]
    [InlineData("nářadí", "naradi rucni-naradi elektricke-naradi")]
    [InlineData("elektrické nářadí", "elektricke-naradi")]
    [InlineData("nářadí -ruční", "naradi elektricke-naradi")]
    [InlineData("pily a pilníky", "pily")]
    [InlineData("vrtačka", "vrtacky")]
    [InlineData("cvočky", "hrebiky")]
    [InlineData("kombinačky", "kleste")]
    [InlineData("šroubovák plochý", "")]
    [InlineData("-šrouby", "")]
    public void The_tool_shop_suggests(string typed, string slugs) =>
        Assert.Equal(slugs, Suggested(ToolShop, typed, czechForms: true));

    [Fact]
    public void The_category_called_what_was_typed_comes_first() =>
        Assert.Equal("naradi rucni-naradi", Suggested(TreeOf([("rucni-naradi", "Ruční nářadí", null, null), ("naradi", "Nářadí", null, null)]), "nářadí", czechForms: true));

    [Fact]
    public void Every_word_as_typed_comes_before_beginnings() =>
        Assert.Equal(
            "male-kleste klesticky",
            Suggested(TreeOf([("klesticky", "Kleštičky malé", null, null), ("male-kleste", "Malé kleště a nůžky", null, null)]), "malé kleště", czechForms: true));

    [Theory]
    [InlineData("šroubováky")]
    [InlineData("šroub")]
    public void A_name_comes_before_keywords(string typed) =>
        Assert.Equal(
            "sroubovaky darky",
            Suggested(TreeOf([("darky", "Dárky", null, "šroubováky v dárkovém balení"), ("sroubovaky", "Šroubováky", null, null)]), typed, czechForms: true));

    [Fact]
    public void Five_at_most_in_the_shops_order() =>
        Assert.Equal(
            "sady-1 sady-2 sady-3 sady-4 sady-5",
            Suggested(TreeOf([.. Enumerable.Range(1, 7).Select(number => ($"sady-{number}", $"Sady {number}", (string?)null, (string?)null))]), "sady", czechForms: true));

    [Fact]
    public void A_hyphenated_word_is_found_whole_and_by_its_parts()
    {
        var tree = TreeOf([("aku-vrtacky", "Aku-vrtačky", null, null)]);

        Assert.Equal("aku-vrtacky", Suggested(tree, "vrtačky", czechForms: true));
        Assert.Equal("aku-vrtacky", Suggested(tree, "aku-vrt", czechForms: true));
    }

    [Fact]
    public void A_code_is_matched_as_a_word_is() =>
        Assert.Equal("m8", Suggested(TreeOf([("m8", "Šrouby M8", null, null)]), "M8", czechForms: true));

    // A language PostgreSQL stems is matched by beginnings as typed: "chair" is the beginning of "chairs", and not
    // of "armchairs".
    [Fact]
    public void A_stemmed_language_matches_beginnings_as_typed()
    {
        var tree = TreeOf([("chairs", "Chairs", null, null), ("armchairs", "Armchairs", null, null)]);

        Assert.Equal("chairs", Suggested(tree, "chair", czechForms: false));
        Assert.Equal("chairs", Suggested(tree, "chairs", czechForms: false));
    }

    private static string Suggested(StoreCategoryTree tree, string typed, bool czechForms) =>
        string.Join(" ", CategorySuggestions.For(tree, SearchTokens.Read(typed, unfinished: true), czechForms).Select(category => category.Slug));

    private static StoreCategoryTree TreeOf(IReadOnlyList<(string Slug, string Name, string? ParentSlug, string? Keywords)> categories)
    {
        var ids = categories.ToDictionary(category => category.Slug, _ => Guid.NewGuid());

        return new StoreCategoryTree(
        [
            .. categories.Select(category => new CategoryRow(
                ids[category.Slug], category.ParentSlug is { } parent ? ids[parent] : null, category.Name, category.Slug, null, null, null, category.Keywords)),
        ]);
    }
}

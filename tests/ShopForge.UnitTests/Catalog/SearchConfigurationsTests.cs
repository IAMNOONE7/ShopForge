using ShopForge.Catalog.Search;

namespace ShopForge.UnitTests.Catalog;

// A culture names the configuration that reads its words, and the folded copy of it that the storefront searches
// with (D-178, D-224).
public sealed class SearchConfigurationsTests
{
    [Theory]
    [InlineData("cs-CZ", "simple", "catalog.simple_folded")]
    [InlineData("sk-SK", "simple", "catalog.simple_folded")]
    [InlineData("pl", "simple", "catalog.simple_folded")]
    [InlineData("en-IE", "english", "catalog.english_folded")]
    [InlineData("de-AT", "german", "catalog.german_folded")]
    [InlineData("RU-ru", "russian", "catalog.russian_folded")]
    [InlineData(null, "simple", "catalog.simple_folded")]
    [InlineData(" ", "simple", "catalog.simple_folded")]
    public void A_culture_names_its_configuration_and_the_folded_copy_of_it(string? culture, string configuration, string folded)
    {
        Assert.Equal(configuration, SearchConfigurations.For(culture));
        Assert.Equal(folded, SearchConfigurations.Folded(culture));
    }

    // The list the database is checked against: one folded copy of each configuration a culture can name, `simple`
    // among them, and no other.
    [Fact]
    public void Every_configuration_a_culture_can_name_has_a_folded_copy()
    {
        var cultures = new[] { "da", "de", "en", "es", "fi", "fr", "hu", "it", "nl", "no", "pt", "ro", "ru", "sv", "tr", "cs" };

        Assert.Equal(
            cultures.Select(SearchConfigurations.Folded).Distinct().Order(StringComparer.Ordinal),
            SearchConfigurations.AllFolded);
        Assert.Equal(16, SearchConfigurations.AllFolded.Count);
    }

    [Fact]
    public void Codes_are_read_folded_and_never_stemmed() =>
        Assert.Equal("catalog.simple_folded", SearchConfigurations.FoldedWithoutStemming);
}

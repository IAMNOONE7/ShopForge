using ShopForge.Catalog.Search;

namespace ShopForge.UnitTests.Catalog;

// What somebody typed, read as words, phrases, codes and excluded words (D-225). Anything that is not a letter or a
// digit is a separator, so nothing typed reaches the query's own syntax.
public sealed class SearchTokensTests
{
    [Theory]
    [InlineData("šroubovák", "word:šroubovák")]
    [InlineData("Šroubovák Plochý", "word:šroubovák word:plochý")]
    [InlineData("šroubovák s magnetem", "word:šroubovák word:magnetem")]
    [InlineData("s", "word:s")]
    [InlineData("a b", "word:a word:b")]
    [InlineData("\"sada bitů\"", "phrase:sada+bitů")]
    [InlineData("\"sada bitů", "phrase:sada+bitů")]
    [InlineData("\"\" chair", "word:chair")]
    [InlineData("šroubovák -sada", "word:šroubovák excluded:sada")]
    [InlineData("-sada", "excluded:sada")]
    [InlineData("--sada", "word:sada")]
    [InlineData("šroubovák- sada", "word:šroubovák word:sada")]
    [InlineData("aku-vrtačka", "word:aku-vrtačka")]
    [InlineData("AKU-VRTAČKA", "word:aku-vrtačka")]
    [InlineData("aku vrtačka", "word:aku word:vrtačka")]
    public void Words_phrases_and_exclusions_are_read_as_typed(string typed, string read) =>
        Assert.Equal(read, Described(SearchTokens.Read(typed)));

    // A code is matched as typed: anything with a digit in it, or capitals around a hyphen.
    [Theory]
    [InlineData("PH2", "code:ph2")]
    [InlineData("4x40", "code:4x40")]
    [InlineData("T20", "code:t20")]
    [InlineData("100 mm", "code:100 word:mm")]
    [InlineData("TS-SK-PH2100", "code:ts-sk-ph2100")]
    [InlineData("WH-BOARD-OAK", "code:wh-board-oak")]
    [InlineData("wh-board-oak", "word:wh-board-oak")]
    [InlineData("5", "code:5")]
    [InlineData("x 5", "code:5")]
    public void Codes_are_told_apart_from_words(string typed, string read) =>
        Assert.Equal(read, Described(SearchTokens.Read(typed)));

    [Theory]
    [InlineData("walnut!!! & <>", "word:walnut")]
    [InlineData(":&|!()\\*'", "")]
    [InlineData("šroub:*A | !(vrták)", "word:šroub word:vrták")]
    [InlineData("it's", "word:it")]
    [InlineData("O'Brien", "word:brien")]
    [InlineData("šroubovák😀", "word:šroubovák")]
    [InlineData("😀😀", "")]
    [InlineData("日本 ёлка", "word:日本 word:ёлка")]
    [InlineData("%L %s", "word:l word:s")]
    public void Anything_but_letters_and_digits_is_a_separator(string typed, string read) =>
        Assert.Equal(read, Described(SearchTokens.Read(typed)));

    [Fact]
    public void A_pasted_paragraph_is_cut_to_the_first_words()
    {
        var typed = string.Join(" ", Enumerable.Range(1, 30).Select(index => $"slovo{(char)('a' + (index % 26))}"));

        Assert.Equal(SearchTokens.MostTokens, SearchTokens.Read(typed).Count);
    }

    private static string Described(IReadOnlyList<SearchToken> tokens) =>
        string.Join(" ", tokens.Select(token => $"{token.Kind.ToString().ToLowerInvariant()}:{string.Join("+", token.Words)}"));
}

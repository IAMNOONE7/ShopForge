using System.Text.RegularExpressions;
using ShopForge.Catalog.Search;

namespace ShopForge.UnitTests.Catalog;

// The questions a search asks, as templates PostgreSQL fills (D-225). A template is operators and placeholders alone,
// and every word it needs travels beside it.
public sealed partial class SearchMatchTests
{
    [Fact]
    public void A_czech_word_is_found_by_its_beginnings_and_ranked_by_its_forms()
    {
        var match = SearchMatch.For(SearchTokens.Read("šroubky"), czechForms: true);

        Asks(match.Found, "(%L:* | %L:*)", "šroubk", "šroubek");
        Asks(match.ExactInName, "%L:A", "šroubky");
        Asks(match.BeginningsInName, "(%L:*A | %L:*A)", "šroubk", "šroubek");
        Assert.Equal(2 * CzechWordForms.Endings.Count, match.FormsInName.Words.Count);
        Assert.Contains("šroubky", match.FormsInName.Words);
        Assert.Contains("šroubek", match.FormsInName.Words);
    }

    // A language PostgreSQL stems reads the word as its stem in every question, and a beginning still finds more.
    [Fact]
    public void A_word_in_a_stemmed_language_is_left_to_its_stemmer()
    {
        var match = SearchMatch.For(SearchTokens.Read("chairs"), czechForms: false);

        Asks(match.Found, "(%L:*)", "chairs");
        Asks(match.FormsInName, "%L:A", "chairs");
    }

    // A code is read apart from the words, without stemming in any language, so it is matched as the index holds it.
    [Fact]
    public void A_code_is_matched_as_typed_and_a_phrase_word_by_word()
    {
        var match = SearchMatch.For(SearchTokens.Read("PH2 \"sada bitů\""), czechForms: true);

        Asks(match.Found, "(%L <-> %L)", "sada", "bitů");
        AsksCodes(match.Found, "%L", "ph2");

        foreach (var tier in new[] { match.ExactInName, match.FormsInName, match.BeginningsInName })
        {
            Asks(tier, "(%L:A <-> %L:A)", "sada", "bitů");
            AsksCodes(tier, "%L:A", "ph2");
        }

        Asks(match.InWhatItIsCalled, "(%L:AB <-> %L:AB)", "sada", "bitů");
        AsksCodes(match.InWhatItIsCalled, "%L:AB", "ph2");
    }

    // What else a product is called, its keywords, brand and categories, sits at weight B beside the name's A.
    [Fact]
    public void A_word_is_also_asked_of_what_else_the_product_is_called()
    {
        var match = SearchMatch.For(SearchTokens.Read("šroubky"), czechForms: true);

        Asks(match.InWhatItIsCalled, "(%L:*AB | %L:*AB)", "šroubk", "šroubek");
        Assert.Empty(match.InWhatItIsCalled.Codes);
    }

    [Fact]
    public void A_search_of_codes_alone_finds_something() =>
        Assert.False(SearchMatch.For(SearchTokens.Read("PH2"), czechForms: true).FindsNothing);

    // An excluded word only narrows what is found; it says nothing about how well the rest answers.
    [Fact]
    public void An_excluded_word_is_left_out_of_the_ranking()
    {
        var match = SearchMatch.For(SearchTokens.Read("šroubovák -sada"), czechForms: true);

        Asks(match.Found, "(%L:*) & !%L", "šroubovák", "sada");
        Asks(match.ExactInName, "%L:A", "šroubovák");
    }

    [Fact]
    public void Nothing_to_match_finds_nothing() =>
        Assert.True(SearchMatch.For(SearchTokens.Read("!!! 😀"), czechForms: true).FindsNothing);

    // Whatever is typed, a template holds placeholders and operators and nothing else, one placeholder a word.
    [Theory]
    [InlineData("šroub:*A | !(vrták) & 'x' <-> \\")]
    [InlineData("%L%s%1$s %%")]
    [InlineData("\"a' | b\" -c'd PH2:A")]
    [InlineData("Robert'); DROP TABLE store_products;--")]
    public void Nothing_typed_reaches_a_template(string typed)
    {
        foreach (var czech in new[] { true, false })
        {
            var match = SearchMatch.For(SearchTokens.Read(typed), czech);

            Assert.All(
                new[] { match.Found, match.ExactInName, match.FormsInName, match.BeginningsInName, match.InWhatItIsCalled },
                question =>
                {
                    Assert.Matches(Operators(), question.Template);
                    Assert.Matches(Operators(), question.CodesTemplate);
                    Assert.Equal(question.Words.Count, Placeholders().Count(question.Template));
                    Assert.Equal(question.Codes.Count, Placeholders().Count(question.CodesTemplate));
                    Assert.All(question.Words.Concat(question.Codes), word => Assert.Matches(@"^[\p{L}\p{Nd}]+(-[\p{L}\p{Nd}]+)*$", word));
                });
        }
    }

    private static void Asks(SearchQueryText question, string template, params string[] words)
    {
        Assert.Equal(template, question.Template);
        Assert.Equal(words, question.Words);
    }

    private static void AsksCodes(SearchQueryText question, string template, params string[] codes)
    {
        Assert.Equal(template, question.CodesTemplate);
        Assert.Equal(codes, question.Codes);
    }

    [GeneratedRegex(@"^((%L(:\*?(AB|A)?)?)|[()|&!<\->\s])*$")]
    private static partial Regex Operators();

    [GeneratedRegex("%L")]
    private static partial Regex Placeholders();
}

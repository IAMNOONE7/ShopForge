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

    [Fact]
    public void A_code_is_matched_as_typed_and_a_phrase_word_by_word()
    {
        var match = SearchMatch.For(SearchTokens.Read("PH2 \"sada bitů\""), czechForms: true);

        Asks(match.Found, "%L & (%L <-> %L)", "ph2", "sada", "bitů");

        foreach (var tier in new[] { match.ExactInName, match.FormsInName, match.BeginningsInName })
        {
            Asks(tier, "%L:A & (%L:A <-> %L:A)", "ph2", "sada", "bitů");
        }
    }

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
                new[] { match.Found, match.ExactInName, match.FormsInName, match.BeginningsInName },
                question =>
                {
                    Assert.Matches(Operators(), question.Template);
                    Assert.Equal(question.Words.Count, Placeholders().Count(question.Template));
                    Assert.All(question.Words, word => Assert.Matches(@"^[\p{L}\p{Nd}]+(-[\p{L}\p{Nd}]+)*$", word));
                });
        }
    }

    private static void Asks(SearchQueryText question, string template, params string[] words)
    {
        Assert.Equal(template, question.Template);
        Assert.Equal(words, question.Words);
    }

    [GeneratedRegex(@"^((%L(:\*?A?|:\*)?)|[()|&!<\->\s])*$")]
    private static partial Regex Operators();

    [GeneratedRegex("%L")]
    private static partial Regex Placeholders();
}

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

    // A word somebody may still be typing is found by its beginnings or by its beginning as typed, read without stemming,
    // wherever a beginning finds it; the words before it are asked for as a search asks (D-227).
    [Fact]
    public void An_unfinished_word_is_also_asked_for_as_typed()
    {
        var match = SearchMatch.For(SearchTokens.Read("dubový st", unfinished: true), czechForms: true);

        Asks(match.Found, "(%L:* | %L:*)", "dubov", "dubův");
        AsksUnfinished(match.Found, "(%L:*)", ["st"], "%L:*", "st");
        Asks(match.ExactInName, "%L:A & %L:A", "dubový", "st");
        Assert.Null(match.ExactInName.Unfinished);
        Assert.Null(match.FormsInName.Unfinished);
        Assert.Contains("st", match.FormsInName.Words);
        AsksUnfinished(match.BeginningsInName, "(%L:*A)", ["st"], "%L:*A", "st");
        AsksUnfinished(match.InWhatItIsCalled, "(%L:*AB)", ["st"], "%L:*AB", "st");
    }

    // English drops "so" as a stop word, which would lose the sofa somebody is typing.
    [Fact]
    public void An_unfinished_word_in_a_stemmed_language_is_found_either_way()
    {
        var match = SearchMatch.For(SearchTokens.Read("so", unfinished: true), czechForms: false);

        Asks(match.Found, "");
        AsksUnfinished(match.Found, "(%L:*)", ["so"], "%L:*", "so");
        Assert.False(match.FindsNothing);
    }

    [Fact]
    public void A_czech_word_unfinished_is_found_by_the_beginnings_of_its_forms() =>
        AsksUnfinished(SearchMatch.For(SearchTokens.Read("šroubky", unfinished: true), czechForms: true).Found,
            "(%L:* | %L:*)", ["šroubk", "šroubek"], "%L:*", "šroubky");

    [Fact]
    public void An_unfinished_code_is_the_beginning_of_a_code()
    {
        var match = SearchMatch.For(SearchTokens.Read("TS-SK-PH21", unfinished: true), czechForms: true);

        AsksCodes(match.Found, "%L:*", "ts-sk-ph21");

        foreach (var tier in new[] { match.ExactInName, match.FormsInName, match.BeginningsInName })
        {
            AsksCodes(tier, "%L:*A", "ts-sk-ph21");
        }

        AsksCodes(match.InWhatItIsCalled, "%L:*AB", "ts-sk-ph21");
    }

    // Whatever is typed, a template holds placeholders and operators and nothing else, one placeholder a word.
    [Theory]
    [InlineData("šroub:*A | !(vrták) & 'x' <-> \\")]
    [InlineData("%L%s%1$s %%")]
    [InlineData("\"a' | b\" -c'd PH2:A")]
    [InlineData("Robert'); DROP TABLE store_products;--")]
    [InlineData("šroub:*A | !(vrták) & 'x' <-> \\ ab")]
    public void Nothing_typed_reaches_a_template(string typed)
    {
        foreach (var (czech, unfinished) in new[] { (true, false), (false, false), (true, true), (false, true) })
        {
            var match = SearchMatch.For(SearchTokens.Read(typed, unfinished), czech);

            Assert.All(
                new[] { match.Found, match.ExactInName, match.FormsInName, match.BeginningsInName, match.InWhatItIsCalled },
                question =>
                {
                    var beginning = question.Unfinished ?? new SearchBeginning("", [], "%L", "x");

                    Assert.All([question.Template, question.CodesTemplate, beginning.Template, beginning.AsTypedTemplate], template => Assert.Matches(Operators(), template));
                    Assert.Equal(question.Words.Count, Placeholders().Count(question.Template));
                    Assert.Equal(question.Codes.Count, Placeholders().Count(question.CodesTemplate));
                    Assert.Equal(beginning.Words.Count, Placeholders().Count(beginning.Template));
                    Assert.Equal(1, Placeholders().Count(beginning.AsTypedTemplate));
                    Assert.All(question.Words.Concat(question.Codes).Concat(beginning.Words).Append(beginning.AsTyped), word => Assert.Matches(@"^[\p{L}\p{Nd}]+(-[\p{L}\p{Nd}]+)*$", word));
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

    private static void AsksUnfinished(SearchQueryText question, string template, string[] words, string asTypedTemplate, string asTyped)
    {
        Assert.NotNull(question.Unfinished);
        Assert.Equal((template, asTypedTemplate, asTyped), (question.Unfinished.Template, question.Unfinished.AsTypedTemplate, question.Unfinished.AsTyped));
        Assert.Equal(words, question.Unfinished.Words);
    }

    [GeneratedRegex(@"^((%L(:\*?(AB|A)?)?)|[()|&!<\->\s])*$")]
    private static partial Regex Operators();

    [GeneratedRegex("%L")]
    private static partial Regex Placeholders();
}

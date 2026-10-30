using ShopForge.Catalog.Search;

namespace ShopForge.UnitTests.Catalog;

// A Czech word typed into a search, turned into the beginnings that find its other forms (D-225). Each row is typed
// with accents and without, as shoppers type both.
public sealed class CzechWordFormsTests
{
    [Theory]
    [InlineData("šroubováky", "šroubovák")]
    [InlineData("šroubováků", "šroubovák")]
    [InlineData("šroubovákem", "šroubovák")]
    [InlineData("šroubovák", "šroubovák")]
    [InlineData("sroubovaky", "sroubovak")]
    [InlineData("sroubovaku", "sroubovak")]
    [InlineData("sroubovakem", "sroubovak")]
    [InlineData("klíče", "klíč")]
    [InlineData("klíčů", "klíč")]
    [InlineData("klice", "klic")]
    [InlineData("šrouby", "šroub")]
    [InlineData("plochých", "ploch")]
    [InlineData("plochych", "ploch")]
    [InlineData("křížový", "kříž")]
    [InlineData("krizovy", "kriz")]
    [InlineData("kladiva", "kladiv")]
    [InlineData("dřevo", "dřev")]
    [InlineData("pilník", "pilník")]
    [InlineData("materiál", "materiál")]
    public void A_word_is_found_by_its_stem(string typed, string stem) =>
        Assert.Equal([stem], CzechWordForms.PrefixesOf(typed));

    // Only endings come off: Lucene's last step, which turns `klíč` into `klík`, would be the beginning of no form of
    // it at all.
    [Fact]
    public void A_stem_is_never_rewritten() =>
        Assert.Equal(["klíč"], CzechWordForms.PrefixesOf("klíče"));

    // The mobile e and ů for o, which a beginning cannot reach, are extra beginnings.
    [Theory]
    [InlineData("šroubky", "šroubk", "šroubek")]
    [InlineData("šroubek", "šroubek", "šroubk")]
    [InlineData("hrnky", "hrnk", "hrnek")]
    [InlineData("vrtačky", "vrtačk", "vrtaček")]
    [InlineData("stoly", "stol", "stůl")]
    [InlineData("stůl", "stůl", "stol")]
    [InlineData("nože", "nož", "nůž")]
    [InlineData("nůž", "nůž", "nož")]
    [InlineData("stul", "stul", "stol")]
    [InlineData("noze", "noz", "nůz")]
    public void An_alternation_is_undone_as_a_second_beginning(string typed, string stem, string alternative) =>
        Assert.Equal([stem, alternative], CzechWordForms.PrefixesOf(typed));

    // A word with an accent anywhere was typed with its accents, so a plain u in it is a u.
    [Fact]
    public void A_plain_u_in_a_word_typed_with_accents_stays_a_u() =>
        Assert.Equal(["šroub"], CzechWordForms.PrefixesOf("šrouby"));

    [Theory]
    [InlineData("šr")]
    [InlineData("sr")]
    [InlineData("x")]
    public void A_word_under_three_letters_is_its_own_beginning(string typed) =>
        Assert.Equal([typed], CzechWordForms.PrefixesOf(typed));

    // Three letters is short enough that no ending comes off: `nůž` keeps its ž, and `pes` does not find `psa`.
    [Theory]
    [InlineData("pes")]
    [InlineData("psy")]
    [InlineData("kov")]
    public void A_word_of_three_letters_loses_no_ending(string typed) =>
        Assert.Equal(typed, CzechWordForms.PrefixesOf(typed)[0]);

    // Each length of ending comes off only a word longer than Lucene's limit for it, so a short word keeps letters
    // that only look like an ending.
    [Theory]
    [InlineData("kuřatech", "kuř")]
    [InlineData("platech", "plat")]
    [InlineData("šatech", "šat")]
    [InlineData("kuřetem", "kuř")]
    [InlineData("kuřatům", "kuř")]
    [InlineData("dětem", "dět")]
    [InlineData("sletem", "slet")]
    [InlineData("marina", "marin")]
    [InlineData("plech", "plech")]
    [InlineData("plat", "plat")]
    public void An_ending_comes_off_only_a_word_long_enough_to_have_it(string typed, string stem) =>
        Assert.Equal([stem], CzechWordForms.PrefixesOf(typed));

    [Fact]
    public void The_endings_of_a_form_are_written_as_the_index_holds_them()
    {
        var endings = CzechWordForms.Endings;

        Assert.Equal("", endings[0]);
        Assert.Subset(new HashSet<string>(endings), new HashSet<string> { "y", "u", "em", "ech", "ych", "ami", "ove", "atech", "uv" });
        Assert.All(endings, ending => Assert.DoesNotMatch(@"[^a-z]", ending));
        Assert.Equal(endings.Count, endings.Distinct().Count());
    }
}

using System.Globalization;
using System.Net;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Search;

// The search's corpus (D-225): what a Czech shopper types into a tool shop, and what has to come first. Each row
// names the products that open the results by code, in groups separated by `|`: a group's products come before the
// next group's, in any order among themselves, because they answer for the same reason and only `ts_rank` and the
// shop's order tell them apart. Each later slice adds to it.
public sealed class SearchCorpusTests(ToolShop shop, ShopForgeApiFactory factory) : IClassFixture<ToolShop>
{
    [Theory]
    // The owner's examples: forms, accents and capitals all find the screwdrivers, names first.
    [InlineData("šroubovák", "TS-SP-55125 TS-SK-PH2100 TS-ST-T20100 TS-SR-1 | TS-SS-6")]
    [InlineData("sroubovak", "TS-SP-55125 TS-SK-PH2100 TS-ST-T20100 TS-SR-1 | TS-SS-6")]
    [InlineData("ŠROUBOVÁK", "TS-SP-55125 TS-SK-PH2100 TS-ST-T20100 TS-SR-1 | TS-SS-6")]
    [InlineData("šroubováky", "TS-SP-55125 TS-SK-PH2100 TS-ST-T20100 TS-SR-1 TS-SS-6")]
    [InlineData("sroubovaky", "TS-SP-55125 TS-SK-PH2100 TS-ST-T20100 TS-SR-1 TS-SS-6")]
    [InlineData("šroubováků", "TS-SS-6 | TS-SP-55125 TS-SK-PH2100 TS-ST-T20100 TS-SR-1")]
    [InlineData("šroubovákem", "TS-SP-55125 TS-SK-PH2100 TS-ST-T20100 TS-SR-1 TS-SS-6")]
    // Screws before screwdrivers: the word itself, then its forms, then whatever only begins with it.
    [InlineData("šrouby", "TS-SD-440 | TS-SM-840")]
    [InlineData("šroub", "TS-SM-840 | TS-SD-440")]
    // Alternations a beginning cannot reach: the mobile e, and ů for o.
    [InlineData("šroubky", "TS-SM3-10")]
    [InlineData("šroubek", "TS-SM3-10")]
    [InlineData("stoly", "TS-SSD-2 | TS-PS-120")]
    [InlineData("stůl", "TS-PS-120 | TS-SSD-2")]
    [InlineData("nože", "TS-NZ-18 TS-NC-18")]
    [InlineData("nůž", "TS-NZ-18 | TS-NC-18")]
    [InlineData("hrnky", "TS-HL-1")]
    // No stem is rewritten: `klíče` is `klíč`.
    [InlineData("klíče", "TS-KN-250 TS-KO-13 TS-SKO-819 TS-KI-6 TS-KM-12")]
    [InlineData("klíč", "TS-KN-250 TS-KO-13 TS-KI-6 TS-KM-12 | TS-SKO-819")]
    [InlineData("klic", "TS-KN-250 TS-KO-13 TS-KI-6 TS-KM-12 | TS-SKO-819")]
    [InlineData("kleště", "TS-KK-180 TS-KS-160 TS-KSI-250")]
    [InlineData("kleste", "TS-KK-180 TS-KS-160 TS-KSI-250")]
    [InlineData("kombinované kleště", "TS-KK-180")]
    // Within a reason, the better match first, even over a product the shop puts first.
    [InlineData("kladivo", "TS-KT-600 | TS-KZ-500")]
    [InlineData("kladiva", "TS-KT-600 | TS-KZ-500")]
    // A short stem's beginnings come after the word and its forms.
    [InlineData("pila", "TS-PO-400 | TS-PZ-300 TS-PP-200")]
    [InlineData("pilka", "TS-PZ-300")]
    [InlineData("pilník", "TS-PP-200")]
    [InlineData("vrtačky", "TS-AV-18 TS-PV-750")]
    [InlineData("vrták", "TS-VB-8120")]
    [InlineData("aku vrtačka", "TS-AV-18")]
    [InlineData("aku-vrtačka", "TS-AV-18")]
    [InlineData("hřebíky", "TS-HS-70")]
    [InlineData("hrebik", "TS-HS-70 | TS-KT-600")]
    [InlineData("matice", "TS-MM8")]
    [InlineData("bruska", "TS-UB-125")]
    [InlineData("úhlová", "TS-UB-125")]
    [InlineData("čepele", "TS-NC-18 | TS-NZ-18")]
    [InlineData("dřevo", "TS-SD-440 TS-VD-560")]
    [InlineData("do dřeva", "TS-SD-440 TS-VD-560")]
    [InlineData("plochý", "TS-SP-55125 TS-PP-200")]
    [InlineData("ploché šroubováky", "TS-SP-55125 | TS-SS-6")]
    [InlineData("pozinkovaný", "TS-SM-840 | TS-MM8")]
    [InlineData("magnetický držák", "TS-MD-50 | TS-SB-32")]
    [InlineData("momentový klíč", "TS-KM-12")]
    [InlineData("torx", "TS-ST-T20100 TS-VD-560 | TS-SB-32")]
    // Incomplete words: a beginning of two letters is a search of its own, with or without the accent.
    [InlineData("šr", "TS-SP-55125 TS-SK-PH2100 TS-ST-T20100 TS-SS-6 TS-SR-1 TS-SM-840 TS-SD-440 TS-SM3-10")]
    [InlineData("sr", "TS-SP-55125 TS-SK-PH2100 TS-ST-T20100 TS-SS-6 TS-SR-1 TS-SM-840 TS-SD-440 TS-SM3-10")]
    [InlineData("imbus", "TS-KI-6 | TS-SB-32")]
    // Codes, typed whole or in part, are matched as typed.
    // The product whose code it is comes before the one whose name only mentions it.
    [InlineData("TS-SK-PH2100", "TS-SK-PH2100 | TS-NH-PH2")]
    [InlineData("ts-sk-ph2100", "TS-SK-PH2100 | TS-NH-PH2")]
    [InlineData("PH2", "TS-SK-PH2100 TS-NH-PH2")]
    [InlineData("T20", "TS-ST-T20100")]
    [InlineData("4x40", "TS-SD-440")]
    [InlineData("100 mm", "TS-SK-PH2100 TS-ST-T20100 TS-VD-560")]
    public async Task What_a_shopper_types_finds_what_they_meant_first(string typed, string first)
    {
        var found = await shop.SearchAsync(typed);
        var position = 0;

        foreach (var group in first.Split('|', StringSplitOptions.TrimEntries).Select(group => group.Split(' ')))
        {
            Assert.Equal(group.Order(StringComparer.Ordinal), found.Skip(position).Take(group.Length).Order(StringComparer.Ordinal));
            position += group.Length;
        }
    }

    // Why each answers, read from the ranking itself rather than from the order it makes: the whole code, then every
    // word in the name exactly, by its forms, or only as a beginning, then anywhere else.
    [Theory]
    [InlineData("TS-SK-PH2100", "TS-SK-PH2100:1 TS-NH-PH2:2")]
    [InlineData("ts-sk-ph2100", "TS-SK-PH2100:1 TS-NH-PH2:2")]
    [InlineData("šroubovák", "TS-SP-55125:2 TS-SS-6:3 TS-SB-32:5")]
    [InlineData("šrouby", "TS-SD-440:2 TS-SM-840:3 TS-SM3-10:4 TS-SP-55125:4 TS-MM8:5")]
    [InlineData("imbus", "TS-KI-6:4 TS-SB-32:5")]
    public async Task Each_answer_comes_for_a_reason_the_ranking_names(string typed, string reasons)
    {
        var tiers = await shop.TiersAsync(typed);

        foreach (var (sku, tier) in reasons.Split(' ').Select(reason => (reason.Split(':')[0], int.Parse(reason.Split(':')[1], CultureInfo.InvariantCulture))))
        {
            Assert.Equal((sku, tier), (sku, tiers[sku]));
        }
    }

    // Sometimes the whole answer is the point: a phrase finds itself, an excluded word leaves, a hidden listing never
    // shows, and words found only together find only what has them all.
    [Theory]
    [InlineData("\"sada bitů\"", "TS-SB-32")]
    [InlineData("zkušební", "")]
    [InlineData("šroubovák s magnetem", "TS-SK-PH2100 TS-SB-32 TS-MD-50")]
    [InlineData("chrom vanadová", "TS-SP-55125 TS-SK-PH2100")]
    [InlineData("šestihranný", "TS-KI-6 TS-SM-840 TS-MM8")]
    [InlineData(":&|!()", "")]
    [InlineData("😀😀", "")]
    public async Task Some_searches_find_exactly_these(string typed, string all)
    {
        var found = await shop.SearchAsync(typed);

        Assert.Equal(all.Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal), found.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_search_for_a_screwdriver_finds_no_screws()
    {
        var found = await shop.SearchAsync("šroubovák");

        Assert.DoesNotContain("TS-SM-840", found);
        Assert.DoesNotContain("TS-SD-440", found);
        Assert.DoesNotContain("TS-SM3-10", found);
    }

    [Fact]
    public async Task An_excluded_word_takes_out_what_has_it()
    {
        var found = await shop.SearchAsync("šroubovák -sada");

        Assert.Equal(["TS-SK-PH2100", "TS-SP-55125", "TS-SR-1", "TS-ST-T20100"], found.Take(4).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("TS-SS-6", found);
        Assert.DoesNotContain("TS-SB-32", found);
    }

    // The tiers order the whole set before it is paged, so the pages of a search read as one list.
    [Fact]
    public async Task Pages_of_a_ranked_search_never_repeat_or_skip_an_answer()
    {
        var whole = await shop.SearchAsync("šr");
        var paged = new List<string>();

        for (var page = 1; paged.Count < whole.Count; page++)
        {
            var next = await shop.SearchAsync("šr", $"&pageSize=3&page={page}");
            Assert.NotEmpty(next);
            paged.AddRange(next);
        }

        Assert.Equal(whole, paged);
    }

    // Words that are all excluded say what is not wanted and nothing about what is: the page is the shop's own list,
    // and nobody searched for anything.
    [Fact]
    public async Task Only_excluded_words_are_not_a_search()
    {
        var excluded = await shop.SearchAsync("-sada");
        var everything = await shop.SearchAsync("");

        Assert.Equal(everything, excluded);
    }

    // Whatever the query's own syntax means, typed it is text: nothing typed is refused, and nothing typed fails.
    [Theory]
    [InlineData(":&|!()\\*'")]
    [InlineData("šroub:*A | !(vrták)")]
    [InlineData("%L %s %%")]
    [InlineData("😀😀")]
    [InlineData("\"")]
    public async Task The_query_s_own_syntax_typed_is_only_text(string typed)
    {
        using var shopper = new StorefrontApi(factory, shop.Store);

        using var response = await shopper.GetAsync($"/api/storefront/products?q={Uri.EscapeDataString(typed)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

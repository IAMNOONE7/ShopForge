namespace ShopForge.Catalog.Search;

// Which of PostgreSQL's text search configurations reads a shop's words. A configuration decides how a word is
// reduced to what it has in common with its other forms — "chairs" to "chair" — and the rules are the
// language's, so the shop's culture chooses it (D-178).
//
// PostgreSQL ships dictionaries for a dozen or so languages and not for Czech, which is the shop's first
// market. A language it has no dictionary for gets `simple`: every word indexed as written, no stemming, which
// finds "židle" from "židle" and not from "židlí". That is worse than a Czech dictionary and much better than
// an English one applied to Czech, which would stem Czech words by English rules and lose them.
internal static class SearchConfigurations
{
    private const string NoStemming = "simple";

    // Only the ones PostgreSQL ships, named as it names them. Adding a language means checking the server has
    // it rather than guessing from the culture, and a migration creating its folded copy.
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["da"] = "danish",
        ["de"] = "german",
        ["en"] = "english",
        ["es"] = "spanish",
        ["fi"] = "finnish",
        ["fr"] = "french",
        ["hu"] = "hungarian",
        ["it"] = "italian",
        ["nl"] = "dutch",
        ["no"] = "norwegian",
        ["pt"] = "portuguese",
        ["ro"] = "romanian",
        ["ru"] = "russian",
        ["sv"] = "swedish",
        ["tr"] = "turkish",
    };

    // Every folded configuration a culture can name, for the check that the database has each of them.
    public static IReadOnlyList<string> AllFolded { get; } =
        [.. Known.Values.Append(NoStemming).Distinct().Select(FoldedOf).Order(StringComparer.Ordinal)];

    public static string For(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return NoStemming;
        }

        var language = culture.Split('-')[0];

        return Known.GetValueOrDefault(language, NoStemming);
    }

    // The same configuration with accents folded away in front of its dictionary, so `šroubovák`, `sroubovak`
    // and `ŠROUBOVÁK` are one word to both the index and the query. The migration creates one for each (D-224).
    public static string Folded(string? culture) => FoldedOf(For(culture));

    // For codes, which are words in no language: never stemmed, and folded like everything else.
    public static string FoldedWithoutStemming { get; } = FoldedOf(NoStemming);

    // Czech, which PostgreSQL cannot stem, has its word forms found by the query instead (D-225).
    public static bool ReadsCzechForms(string? culture) =>
        !string.IsNullOrWhiteSpace(culture) && culture.Split('-')[0].Equals("cs", StringComparison.OrdinalIgnoreCase);

    private static string FoldedOf(string configuration) => $"catalog.{configuration}_folded";
}

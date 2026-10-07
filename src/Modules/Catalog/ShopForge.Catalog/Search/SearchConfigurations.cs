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
    // it rather than guessing from the culture.
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

    public static string For(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return NoStemming;
        }

        var language = culture.Split('-')[0];

        return Known.GetValueOrDefault(language, NoStemming);
    }
}

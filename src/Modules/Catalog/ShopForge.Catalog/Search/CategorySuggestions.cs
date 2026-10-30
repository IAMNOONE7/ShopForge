using System.Text.RegularExpressions;
using ShopForge.Catalog.Categories;

namespace ShopForge.Catalog.Search;

// The categories the search box offers for what has been typed so far (D-227). A store has a few hundred categories at
// most, read in one query and matched here rather than by the index: every word typed must begin a word of the
// category's name or keywords, by its Czech forms in a Czech store, and an excluded word may be in neither. The one
// called what was typed comes first, then those with every word in the name as typed, then those with every word's
// beginning in it, then those only their keywords find, each group in the shop's own order.
internal static partial class CategorySuggestions
{
    public const int Most = 5;

    public static IReadOnlyList<CategoryRow> For(StoreCategoryTree categories, IReadOnlyList<SearchToken> tokens, bool czechForms)
    {
        var wanted = tokens
            .Where(token => token.Kind != SearchTokenKind.Excluded)
            .SelectMany(token => token.Words.Select(word => new Wanted(
                Folding.Of(word),
                [.. (czechForms && token.Kind != SearchTokenKind.Code ? CzechWordForms.PrefixesOf(word) : [word]).Select(Folding.Of)])))
            .ToList();
        var excluded = tokens.Where(token => token.Kind == SearchTokenKind.Excluded).Select(token => Folding.Of(token.Text)).ToHashSet();

        if (wanted.Count == 0)
        {
            return [];
        }

        return
        [
            .. categories.InTreeOrder()
                .Select((category, order) => (Category: category, Order: order, Rank: RankOf(category, wanted, excluded)))
                .Where(candidate => candidate.Rank is not null)
                .OrderBy(candidate => candidate.Rank)
                .ThenBy(candidate => candidate.Order)
                .Take(Most)
                .Select(candidate => candidate.Category),
        ];
    }

    private static int? RankOf(CategoryRow category, List<Wanted> wanted, HashSet<string> excluded)
    {
        var name = WordsOf(category.Name);
        var keywords = WordsOf(category.SearchKeywords);

        if (name.Concat(keywords).Any(excluded.Contains))
        {
            return null;
        }

        if (!wanted.All(word => name.Any(word.Begins)))
        {
            return wanted.All(word => name.Concat(keywords).Any(word.Begins)) ? 3 : null;
        }

        if (!wanted.All(word => name.Contains(word.Text)))
        {
            return 2;
        }

        // A one-letter word in a name, as in "Pily a pilníky", is one a search leaves out.
        return name.Where(word => word.Length > 1 && !word.Contains('-')).All(word => wanted.Any(typed => typed.Text == word)) ? 0 : 1;
    }

    // Lower case and folded, as the index has them: a hyphenated word whole and each of its parts, as PostgreSQL's
    // parser reads `aku-vrtačky`.
    private static List<string> WordsOf(string? text) =>
        text is null
            ? []
            : [.. Words().Matches(Folding.Of(text.ToLowerInvariant())).SelectMany(match => match.Value.Contains('-') ? [match.Value, .. match.Value.Split('-')] : new[] { match.Value })];

    [GeneratedRegex(@"[\p{L}\p{Nd}]+(?:-[\p{L}\p{Nd}]+)*")]
    private static partial Regex Words();

    // A word typed, folded, and the beginnings that find its forms.
    private sealed record Wanted(string Text, IReadOnlyList<string> Prefixes)
    {
        public bool Begins(string word) => Prefixes.Any(prefix => word.StartsWith(prefix, StringComparison.Ordinal));
    }
}

using System.Text;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Search;

// What somebody typed, turned into the listings that answer it. The match is an entity query rather than a
// list of ids: it becomes the root of the query the page already runs, so a count, a facet, an order and a
// page of results all read one set (D-178).
internal sealed class SearchTerms
{
    // Long enough to mean something, short enough that nobody is posting a document. A single letter matches
    // most of a catalogue and costs more to rank than it is worth.
    public const int Shortest = 2;
    public const int Longest = 200;

    private const string Matched = """
        SELECT * FROM catalog.store_products AS listing
        WHERE listing.store_id = {0} AND listing.search_vector_folded @@ @@found@@
        """;

    // The tiers say why a listing answers (D-225): its whole code typed, then every word in its name exactly, by its
    // forms, or only as a beginning, then in what else it is called (keywords, brand, categories, D-226), then
    // anywhere else. `ts_rank` orders a tier, and the shop's own order and the id break what is left, so pages never
    // swap answers. A code is a SKU, a barcode or a part number, looked up for each listing found through the index on
    // its product, rather than among every variant of every company on the platform.
    private const string Ranking = """
        SELECT listing.id AS "id",
            CASE
                WHEN EXISTS (
                    SELECT 1 FROM catalog.product_variants AS variant
                    WHERE variant.product_id = listing.product_id
                        AND (lower(variant.sku) = lower({2}) OR variant.ean = {2} OR lower(variant.part_number) = lower({2})))
                    THEN 1
                WHEN listing.search_vector_folded @@ @@exact@@ THEN 2
                WHEN listing.search_vector_folded @@ @@forms@@ THEN 3
                WHEN listing.search_vector_folded @@ @@beginnings@@ THEN 4
                WHEN listing.search_vector_folded @@ @@called@@ THEN 5
                ELSE 6
            END AS "tier",
            ts_rank(listing.search_vector_folded, @@found@@) AS "rank"
        FROM catalog.store_products AS listing
        WHERE listing.store_id = {0} AND listing.search_vector_folded @@ @@found@@
        """;

    private readonly DbContext _dbContext;
    private readonly string _configuration;
    private readonly Guid _storeId;
    private readonly SearchMatch _match;

    private SearchTerms(DbContext dbContext, string configuration, Guid storeId, string typed, IReadOnlyList<SearchToken> tokens, SearchMatch match)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _storeId = storeId;
        _match = match;
        Typed = typed;
        Tokens = tokens;
    }

    public string Typed { get; }

    public IReadOnlyList<SearchToken> Tokens { get; }

    // Null when there is nothing worth searching for, which is how a page with no `q` and a page with `q=`
    // are the same page. Words that are all excluded are not a search either: they say what is not wanted, and
    // nothing about what is. Somebody still typing (D-227) may stop in the middle of the last word, unless a space
    // after it says it is finished.
    public static SearchTerms? Of(DbContext dbContext, IStoreContext storeContext, StoreSettings settings, string? typed, bool typing = false)
    {
        var terms = Tidied(typed);

        if (terms.Length < Shortest)
        {
            return null;
        }

        var tokens = SearchTokens.Read(terms, unfinished: typing && !char.IsWhiteSpace(typed![^1]));

        if (tokens.Count > 0 && tokens.All(token => token.Kind == SearchTokenKind.Excluded))
        {
            return null;
        }

        return new SearchTerms(
            dbContext,
            SearchConfigurations.Folded(settings.Culture),
            storeContext.StoreId ?? Guid.Empty,
            terms,
            tokens,
            SearchMatch.For(tokens, SearchConfigurations.ReadsCzechForms(settings.Culture)));
    }

    // Composed first: a letter typed as a base and a separate accent, as some keyboards and pasted text send it, is
    // the letter itself, so the two spellings are one search and one line in the log (D-224). Then cut to the longest
    // a search reads, never between the two halves of a character beyond the first 65,536, such as an emoji: half of
    // one is not text, and the database's driver refuses to send it, which answered a 500 (D-224).
    public static string Tidied(string? typed)
    {
        var terms = typed?.Normalize(NormalizationForm.FormC).Trim() ?? "";

        return terms.Length > Longest ? terms[..(char.IsHighSurrogate(terms[Longest - 1]) ? Longest - 1 : Longest)].TrimEnd() : terms;
    }

    // The store is named in the statement as a second guard and for the planner's sake, not as the only one:
    // EF applies the tenancy filter around a composed query, so the entity match is scoped whether this
    // clause is here or not — removing it fails no test, which is how I know. The keyless ranking query is
    // not filtered by EF, and it is joined against this one, so neither can widen what the page returns.
    //
    // Something typed with no word in it, only punctuation or emoji, is a search that finds nothing.
    public IQueryable<StoreProduct> Matching() =>
        _match.FindsNothing
            ? _dbContext.Set<StoreProduct>().Where(_ => false)
            : _dbContext.Set<StoreProduct>().FromSqlRaw(Statement(Matched, out var parameters), parameters);

    // How well each one answers, for the order a shopper means by "best first". The column names are the ones
    // the context's snake-case convention expects of a raw projection: EF asks for `id`, not for `Id`.
    public IQueryable<SearchHit> Ranked() =>
        _match.FindsNothing
            ? _dbContext.Database.SqlQueryRaw<SearchHit>("""SELECT NULL::uuid AS "id", 0 AS "tier", 0::real AS "rank" WHERE FALSE""")
            : _dbContext.Database.SqlQueryRaw<SearchHit>(Statement(Ranking, out var parameters), parameters);

    // The statement's own text, with every question written out and numbered; what was typed only ever travels as a
    // parameter. The configuration comes from a fixed table of names, never from a request (D-178).
    private string Statement(string sql, out object[] parameters)
    {
        var values = new List<object> { _storeId, _configuration, Typed, SearchConfigurations.FoldedWithoutStemming };
        var questions = new Dictionary<string, SearchQueryText>
        {
            ["found"] = _match.Found,
            ["exact"] = _match.ExactInName,
            ["forms"] = _match.FormsInName,
            ["beginnings"] = _match.BeginningsInName,
            ["called"] = _match.InWhatItIsCalled,
        };

        foreach (var (name, question) in questions.Where(question => sql.Contains($"@@{question.Key}@@", StringComparison.Ordinal)))
        {
            sql = sql.Replace($"@@{name}@@", Question(question, values), StringComparison.Ordinal);
        }

        parameters = [.. values];

        return sql;
    }

    // One question asked of the folded vector, as the parts it has, all to match: its words read in the store's
    // language ({1}), its codes read without stemming ({3}, D-226), and a word somebody may still be typing, read both
    // ways (D-227). Each part is a template whose words `format` quotes into place (D-225); a part with nothing in it
    // is left out rather than parsed as an empty query. Wrapped in a subquery so it is worked out once for the
    // statement, rather than once for every listing it reads.
    private static string Question(SearchQueryText question, List<object> values)
    {
        var parts = new List<string>();

        if (question.Template.Length > 0)
        {
            parts.Add(Parsed("{1}", question.Template, question.Words, values));
        }

        if (question.CodesTemplate.Length > 0)
        {
            parts.Add(Parsed("{3}", question.CodesTemplate, question.Codes, values));
        }

        if (question.Unfinished is { } unfinished)
        {
            parts.Add($"({Parsed("{1}", unfinished.Template, unfinished.Words, values)} || {Parsed("{3}", unfinished.AsTypedTemplate, [unfinished.AsTyped], values)})");
        }

        return $"(SELECT {string.Join(" && ", parts)})";
    }

    private static string Parsed(string configuration, string template, IReadOnlyList<string> words, List<object> values)
    {
        var parsed = $"to_tsquery({configuration}::regconfig, format({{{values.Count}}}, VARIADIC {{{values.Count + 1}}}::text[]))";
        values.Add(template);
        values.Add(words.ToArray());

        return parsed;
    }
}

// One listing that answers, the tier that says why, and how well within it.
internal sealed record SearchHit(Guid Id, int Tier, float Rank);

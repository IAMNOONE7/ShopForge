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

    private readonly DbContext _dbContext;
    private readonly string _configuration;
    private readonly Guid _storeId;

    private SearchTerms(DbContext dbContext, string configuration, Guid storeId, string typed)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _storeId = storeId;
        Typed = typed;
    }

    public string Typed { get; }

    // Null when there is nothing worth searching for, which is how a page with no `q` and a page with `q=`
    // are the same page.
    public static SearchTerms? Of(DbContext dbContext, IStoreContext storeContext, StoreSettings settings, string? typed)
    {
        var terms = typed?.Trim();

        if (terms is null || terms.Length < Shortest)
        {
            return null;
        }

        if (terms.Length > Longest)
        {
            terms = terms[..Longest];
        }

        return new SearchTerms(
            dbContext,
            SearchConfigurations.For(settings.Culture),
            storeContext.StoreId ?? Guid.Empty,
            terms);
    }

    // `websearch_to_tsquery` is the forgiving one: it takes what a person types — bare words, "a phrase", or
    // -excluded — and never throws on punctuation, which `to_tsquery` does.
    //
    // The store is named in the statement as a second guard and for the planner's sake, not as the only one:
    // EF applies the tenancy filter around a composed query, so the entity match is scoped whether this
    // clause is here or not — removing it fails no test, which is how I know. The keyless ranking query is
    // not filtered by EF, and it is joined against this one, so neither can widen what the page returns.
    public IQueryable<StoreProduct> Matching() =>
        _dbContext.Set<StoreProduct>().FromSql(
            $"SELECT * FROM catalog.store_products AS listing WHERE listing.store_id = {_storeId} AND listing.search_vector @@ websearch_to_tsquery({_configuration}::regconfig, {Typed})");

    // How well each one answers, for the order a shopper means by "best first". The column names are the ones
    // the context's snake-case convention expects of a raw projection: EF asks for `id`, not for `Id`.
    public IQueryable<SearchHit> Ranked() =>
        _dbContext.Database.SqlQuery<SearchHit>(
            $"SELECT listing.id AS \"id\", ts_rank(listing.search_vector, websearch_to_tsquery({_configuration}::regconfig, {Typed})) AS \"rank\" FROM catalog.store_products AS listing WHERE listing.store_id = {_storeId} AND listing.search_vector @@ websearch_to_tsquery({_configuration}::regconfig, {Typed})");
}

// One listing that answers, and how well.
internal sealed record SearchHit(Guid Id, float Rank);

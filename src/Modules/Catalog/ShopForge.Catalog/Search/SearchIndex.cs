using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Search;

// What a shopper's typing is matched against, kept on the listing as a `tsvector` and rebuilt from the data
// whenever the data changes. One statement, so there is one place that decides what "searchable" means and no
// chance of two of them disagreeing (D-178).
//
// The weights are PostgreSQL's own: a hit in a name counts for more than one in a description, and a code
// somebody typed exactly counts for as much as a name. Codes are indexed with no stemming whatever the shop's
// language is, because "FURN-OAK-CHAIR" is not a word in any of them.
internal sealed class SearchIndex(DbContext dbContext, IStoreContext storeContext, ICurrentStoreSettings settings)
{
    private const string Statement = """
        UPDATE catalog.store_products AS listing
        SET search_vector =
            setweight(to_tsvector('@@config@@', coalesce(listing.name, '')), 'A')
            || setweight(to_tsvector('simple', coalesce((
                SELECT string_agg(variant.sku, ' ')
                FROM catalog.product_variants AS variant
                WHERE variant.product_id = listing.product_id), '')), 'A')
            || setweight(to_tsvector('@@config@@', coalesce(listing.description, '')), 'C')
            || setweight(to_tsvector('@@config@@', coalesce((
                SELECT string_agg(value.text_value, ' ')
                FROM catalog.product_attribute_values AS value
                JOIN catalog.attribute_definitions AS definition
                    ON definition.id = value.attribute_definition_id AND definition.is_searchable
                WHERE value.store_product_id = listing.id), '')), 'D')
        WHERE listing.store_id = {0}
        """;

    public Task RefreshAsync(Guid storeProductId, CancellationToken cancellationToken) =>
        RefreshAsync([storeProductId], cancellationToken);

    public async Task RefreshAsync(IReadOnlyCollection<Guid> storeProductIds, CancellationToken cancellationToken)
    {
        if (storeProductIds.Count == 0)
        {
            return;
        }

        // Built into a local so the analyser can see there is no interpolation in the call: the statement is
        // this file's own text and the two values are parameters.
        var sql = await StatementAsync(cancellationToken) + " AND listing.id = ANY({1})";

        await dbContext.Database.ExecuteSqlRawAsync(sql, [StoreId, storeProductIds.ToArray()], cancellationToken);
    }

    // Every listing of the store in scope: for a shop whose language or searchable attributes have changed, or
    // whose index has never been built.
    public async Task RefreshStoreAsync(CancellationToken cancellationToken) =>
        await dbContext.Database.ExecuteSqlRawAsync(
            await StatementAsync(cancellationToken), [StoreId], cancellationToken);

    private Guid StoreId => storeContext.StoreId ?? throw new InvalidOperationException("Indexing needs a store in scope.");

    // `to_tsvector` takes the configuration as a literal, so it is put into the statement rather than passed
    // as a parameter. The value comes from a fixed table of PostgreSQL's own names and never from a request,
    // which is what makes that safe (D-178).
    private async Task<string> StatementAsync(CancellationToken cancellationToken)
    {
        var configuration = SearchConfigurations.For((await settings.GetAsync(cancellationToken)).Culture);

        return Statement.Replace("@@config@@", configuration, StringComparison.Ordinal);
    }
}

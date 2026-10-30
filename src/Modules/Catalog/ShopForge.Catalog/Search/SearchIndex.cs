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
//
// What it reads (D-226): the name and every code (SKU, barcode, part number) at A; what the product is otherwise called
// at B, which is the merchant's keywords, the brand and the categories it is filed under, each category's words kept
// ready on the category; the description at C; and at D the text and the option names of attributes marked searchable.
//
// The words are read twice, into two columns: as before, for a previous build still serving through a release
// or brought back by a rollback, and with accents folded, which is what the storefront searches (D-224).
internal sealed class SearchIndex(DbContext dbContext, IStoreContext storeContext, ICurrentStoreSettings settings)
{
    private const string Words = """
        setweight(to_tsvector('@@config@@', coalesce(listing.name, '')), 'A')
            || setweight(to_tsvector('@@codes@@', coalesce((
                SELECT string_agg(concat_ws(' ', variant.sku, variant.ean, variant.part_number), ' ')
                FROM catalog.product_variants AS variant
                WHERE variant.product_id = listing.product_id), '')), 'A')
            || setweight(to_tsvector('@@config@@', concat_ws(' ',
                listing.search_keywords,
                (SELECT product.brand FROM catalog.products AS product WHERE product.id = listing.product_id),
                (SELECT string_agg(category.search_text, ' ')
                 FROM catalog.product_categories AS assignment
                 JOIN catalog.categories AS category ON category.id = assignment.category_id
                 WHERE assignment.store_product_id = listing.id))), 'B')
            || setweight(to_tsvector('@@config@@', coalesce(listing.description, '')), 'C')
            || setweight(to_tsvector('@@config@@', coalesce((
                SELECT string_agg(coalesce(value.text_value, choice.name), ' ')
                FROM catalog.product_attribute_values AS value
                JOIN catalog.attribute_definitions AS definition
                    ON definition.id = value.attribute_definition_id AND definition.is_searchable
                LEFT JOIN catalog.attribute_options AS choice ON choice.id = value.option_id
                WHERE value.store_product_id = listing.id), '')), 'D')
        """;

    private const string Statement = """
        UPDATE catalog.store_products AS listing
        SET search_vector = @@plain@@,
            search_vector_folded = @@folded@@
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
        var culture = (await settings.GetAsync(cancellationToken)).Culture;

        return Statement
            .Replace("@@plain@@", Read(SearchConfigurations.For(culture), "simple"), StringComparison.Ordinal)
            .Replace("@@folded@@", Read(SearchConfigurations.Folded(culture), SearchConfigurations.FoldedWithoutStemming), StringComparison.Ordinal);
    }

    private static string Read(string configuration, string codes) =>
        Words
            .Replace("@@config@@", configuration, StringComparison.Ordinal)
            .Replace("@@codes@@", codes, StringComparison.Ordinal);
}

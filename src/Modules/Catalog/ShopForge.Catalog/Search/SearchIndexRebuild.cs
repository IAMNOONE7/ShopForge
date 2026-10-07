using ShopForge.Shared.Maintenance;

namespace ShopForge.Catalog.Search;

// The nightly rebuild. Every path that changes a listing's own words refreshes it there and then, so this is
// not what makes search work — it is what bounds the cost of a path that forgets, and of the migration that
// added the column to a catalogue already full of products (D-178).
//
// A variant's code is the known case: product endpoints are the tenant's rather than one store's, and the
// index belongs to a store, so a renamed SKU becomes findable here rather than immediately.
internal sealed class SearchIndexRebuild(SearchIndex index) : IStoreMaintenance
{
    public string Name => "Search index";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await index.RefreshStoreAsync(cancellationToken);

        // Nothing is removed, and the housekeeping log counts removals.
        return 0;
    }
}

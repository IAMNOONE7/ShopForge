using ShopForge.Shared.Maintenance;

namespace ShopForge.Catalog.Search;

// The nightly rebuild. Every path that changes a listing's words refreshes it there and then, the import and the
// company-wide product endpoints included (D-201, D-226), so this is not what makes search work: it bounds the cost
// of a path that forgets, and of a migration that meets a catalogue already full of products (D-178).
//
// Each category's words are written first, so a category the paths that keep them did not see, such as one a
// development seed made, is read with the rest (D-226).
internal sealed class SearchIndexRebuild(SearchIndex index, CategorySearchText categories) : IStoreMaintenance
{
    public string Name => "Search index";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await categories.RewriteAsync(cancellationToken);
        await index.RefreshStoreAsync(cancellationToken);

        // Nothing is removed, and the housekeeping log counts removals.
        return 0;
    }
}

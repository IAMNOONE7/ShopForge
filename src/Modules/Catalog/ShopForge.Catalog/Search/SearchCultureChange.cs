using ShopForge.Shared.Messaging;

namespace ShopForge.Catalog.Search;

// A store's culture chooses how its words are read (D-178), and a query is read in the new one the moment the
// change is saved. So every listing is read again in it now, rather than half of search missing until the nightly
// pass: a store set up in English by mistake and then switched to Czech would otherwise lose every stemmed word
// (D-224). Reading them again twice leaves them as reading them once, which is what at-least-once delivery needs.
internal sealed class SearchCultureChange(SearchIndex index) : IEventHandler<StoreCultureChanged>
{
    public Task HandleAsync(StoreCultureChanged domainEvent, CancellationToken cancellationToken) =>
        index.RefreshStoreAsync(cancellationToken);
}

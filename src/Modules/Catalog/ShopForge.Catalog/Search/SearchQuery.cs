using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Search;

// One search somebody ran, and whether the shop had an answer. No customer, no session, no address: the term
// and the count are facts about the catalogue, which is what makes keeping them a report rather than a
// profile (D-178).
internal sealed class SearchQuery : IStoreOwned
{
    private SearchQuery()
    {
    }

    public SearchQuery(Guid storeId, string terms, int found, DateTimeOffset askedAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Terms = terms;
        Found = found;
        AskedAt = askedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Terms { get; private set; } = null!;

    public int Found { get; private set; }

    public DateTimeOffset AskedAt { get; private set; }
}

using ShopForge.Shared.Stores;

namespace ShopForge.Stores.Provisioning;

// The store's own address, which is the primary domain and never the host of whichever request happened to
// ask: a link in an e-mail is followed hours later from an inbox, and a feed is read by somebody else's
// crawler, so neither can be told about an alias a shopper happened to be browsing (D-164).
//
// It comes from the store's settings rather than from a query of its own, because every caller that wants an
// address is already on a page that has read them — and because which domain counts is then said in one place
// (D-173).
internal sealed class StoreUrls(ICurrentStoreSettings settings) : IStoreUrls
{
    public async Task<StoreAddress?> FindAsync(CancellationToken cancellationToken) =>
        (await settings.GetAsync(cancellationToken)).Address;
}

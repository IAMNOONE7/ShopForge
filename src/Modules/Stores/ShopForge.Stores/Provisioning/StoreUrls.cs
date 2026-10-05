using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Provisioning;

// The store's own address, which is the primary domain and never the host of whichever request happened to
// ask: a link in an e-mail is followed hours later from an inbox, and a feed is read by somebody else's
// crawler, so neither can be told about an alias a shopper happened to be browsing.
internal sealed class StoreUrls(DbContext dbContext, IStoreContext storeContext) : IStoreUrls
{
    public async Task<StoreAddress?> FindAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<Store>()
            .Where(store => store.Id == storeContext.StoreId)
            .Select(store => new
            {
                // A domain can only be made primary once it is proved, so this asks for both rather than
                // trusting that rule to stay true somewhere else.
                Host = store.Domains
                    .Where(domain => domain.IsPrimary && domain.VerifiedAt != null)
                    .Select(domain => domain.HostName)
                    .FirstOrDefault(),
                store.Culture,
            })
            .Select(store => store.Host == null ? null : new StoreAddress(store.Host, Language(store.Culture)))
            .SingleOrDefaultAsync(cancellationToken);

    // A culture names a language and a place; a page is written in the language. "cs-CZ" is Czech wherever it
    // is read.
    private static string Language(string culture) => culture.Split('-')[0].ToLowerInvariant();
}

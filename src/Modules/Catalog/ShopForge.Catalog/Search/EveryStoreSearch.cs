using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Search;

// A product belongs to the company and its listings to the stores that sell it, each indexed in its own store's
// language. What changes on the product — its brand, a code, a barcode, a part number — is read again in every store
// that lists it, there and then, each in a scope of that store's own (D-226). This closes I10: a renamed SKU used to
// wait for the nightly pass in every store but the one being edited.
internal sealed class EveryStoreSearch(DbContext dbContext, IStoreContext storeContext, IServiceScopeFactory scopes)
{
    // The products are the company's: the caller found them through the tenancy filter, so every listing of them is in
    // one of the company's stores. The store being edited, if any, can be left out: it indexes its own.
    public async Task RefreshAsync(IReadOnlyCollection<Guid> productIds, Guid? besides, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return;
        }

        var tenantId = storeContext.TenantId ?? throw new InvalidOperationException("Indexing a company's products needs the company in scope.");
        var listings = await dbContext.Set<StoreProduct>()
            .IgnoreQueryFilters([TenancyFilters.Store])
            .Where(listing => productIds.Contains(listing.ProductId) && listing.StoreId != besides)
            .Select(listing => new { listing.Id, listing.StoreId })
            .ToListAsync(cancellationToken);

        foreach (var store in listings.GroupBy(listing => listing.StoreId))
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<StoreContext>().Set(store.Key, tenantId);

            await scope.ServiceProvider.GetRequiredService<SearchIndex>().RefreshAsync([.. store.Select(listing => listing.Id)], cancellationToken);
        }
    }
}

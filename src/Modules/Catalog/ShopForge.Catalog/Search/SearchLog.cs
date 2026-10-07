using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Search;

internal sealed class SearchLog(DbContext dbContext, IStoreContext storeContext, TimeProvider clock) : ISearchLog
{
    public async Task RecordAsync(string terms, int found, CancellationToken cancellationToken)
    {
        if (storeContext.StoreId is not { } storeId)
        {
            return;
        }

        dbContext.Add(new SearchQuery(storeId, terms, found, clock.GetUtcNow()));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

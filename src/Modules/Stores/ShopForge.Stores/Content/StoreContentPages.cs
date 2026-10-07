using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;

namespace ShopForge.Stores.Content;

internal sealed class StoreContentPages(DbContext dbContext) : IStoreContentPages
{
    // A draft is not advertised, and neither is a page that has asked to be left alone — the same two
    // questions a listing is asked before it reaches a sitemap (D-167).
    public async Task<IReadOnlyList<string>> AdvertisedSlugsAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<ContentPage>()
            .AsNoTracking()
            .Where(page => page.IsPublished && !page.SeoNoIndex)
            .OrderBy(page => page.Slug)
            .Select(page => page.Slug)
            .ToListAsync(cancellationToken);
}

using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Feeds;
using ShopForge.Shared.Maintenance;

namespace ShopForge.Catalog.Feeds;

// Every feed a shop has switched on, produced once a day by the maintenance the platform already runs. A
// shopping engine collects on its own schedule and reads whatever was last produced, so nothing here has to
// line up with anybody else's timing (D-168).
internal sealed class FeedRefresh(DbContext dbContext, IProductFeeds feeds, IEnumerable<IProductFeedFormat> formats) : IStoreMaintenance
{
    public string Name => "product-feeds";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var known = formats.Select(format => format.Key).ToList();
        var wanted = await dbContext.Set<StoreFeed>()
            .AsNoTracking()
            .Where(feed => feed.IsEnabled && known.Contains(feed.Feed))
            .Select(feed => feed.Feed)
            .ToListAsync(cancellationToken);
        var produced = 0;

        foreach (var feed in wanted)
        {
            // One failing feed does not stop the others: the engine records its own failure and leaves the
            // last good document where it was.
            if (await feeds.RunAsync(feed, cancellationToken) is not null)
            {
                produced++;
            }
        }

        return produced;
    }
}

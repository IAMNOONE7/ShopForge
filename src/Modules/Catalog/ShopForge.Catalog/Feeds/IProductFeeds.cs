using ShopForge.Catalog.Feeds.Google;

namespace ShopForge.Catalog.Feeds;

internal interface IProductFeeds
{
    // Everything this feed would carry, read without producing anything, so a merchant can be told what
    // Google would refuse before Google refuses it (D-169).
    Task<List<FeedProblem>?> CheckAsync(string feed, CancellationToken cancellationToken);

    // Null when there is nothing to produce: no such feed, the shop has it switched off, or the shop has no
    // address for its products to be advertised at.
    Task<FeedRun?> RunAsync(string feed, CancellationToken cancellationToken);
}

internal sealed record FeedRun(DateTimeOffset At, int Milliseconds, long Bytes, int Products, int Skipped);

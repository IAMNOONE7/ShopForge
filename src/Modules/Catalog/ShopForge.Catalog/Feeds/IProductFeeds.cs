namespace ShopForge.Catalog.Feeds;

internal interface IProductFeeds
{
    // Null when there is nothing to produce: no such feed, the shop has it switched off, or the shop has no
    // address for its products to be advertised at.
    Task<FeedRun?> RunAsync(string feed, CancellationToken cancellationToken);
}

internal sealed record FeedRun(DateTimeOffset At, int Milliseconds, long Bytes, int Products, int Skipped);

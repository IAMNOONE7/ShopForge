namespace ShopForge.Shared.Feeds;

// One shopping engine's idea of what a product feed looks like. Three of them want the same facts in three
// shapes, so the machinery that gathers the facts, stores the document and serves it knows nothing about any
// of the shapes (D-168, and the lesson of D-120: the seam is worth having before the second one arrives).
public interface IProductFeedFormat
{
    // What the store turns on, and what appears in the delivery address.
    string Key { get; }

    string ContentType { get; }

    // Written as the products arrive rather than after, because a shop with fifty thousand of them has a
    // document too large to hold in memory twice.
    Task WriteAsync(Stream destination, FeedStore store, IAsyncEnumerable<FeedProduct> products, CancellationToken cancellationToken);
}

// What every feed says about the shop itself.
public sealed record FeedStore(string Name, string Language, string Currency, string HomeUrl);

// What every feed says about one thing for sale. A format takes what it needs and leaves the rest; what it
// must not do is go looking for more, because then the gathering would differ per format.
public sealed record FeedProduct(
    string Sku,
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    string Url,
    string? ImageUrl,
    string? Brand,
    string? Gtin,
    string? PartNumber,
    string? Condition,
    int Available);

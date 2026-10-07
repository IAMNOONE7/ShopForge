using ShopForge.Shared.Shipping;

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

// What every feed says about the shop itself, including what it charges to deliver — which is only what the
// shop has actually said, never a guess (D-169).
public sealed record FeedStore(
    string Name,
    string Language,
    string Currency,
    string HomeUrl,
    IReadOnlyList<ShippingRate> Shipping);

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
    IReadOnlyList<string> MoreImageUrls,
    string? Brand,
    string? Gtin,
    string? PartNumber,
    string? Condition,
    int Available,

    // The product this form belongs to, and whether it has siblings. A feed ties variants together so a
    // shopping engine shows one thing in several sizes rather than several things (D-169).
    string GroupId,
    bool HasSiblings,

    // What this engine calls the category this thing is in, when the merchant has said. Absent means nobody
    // has mapped it, and a feed leaves the field out rather than publishing the shop's own taxonomy into
    // somebody else's field (D-170).
    string? EngineCategory,

    // The measurements a shop has chosen to publish, in the order it put them in.
    IReadOnlyList<FeedParameter> Parameters);

// One named measurement of a thing, with its unit where it has one: "Width", "45", "cm".
public sealed record FeedParameter(string Name, string Value, string? Unit);

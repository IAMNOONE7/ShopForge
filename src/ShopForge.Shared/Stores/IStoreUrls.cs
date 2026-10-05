namespace ShopForge.Shared.Stores;

// Sitemaps, canonical tags, structured data, shopping feeds and the mail a shop sends all have to name the same
// page by the same address, and the shop's own pages are served by a frontend that decides their shape. That
// makes the shape a contract between the two rather than a detail of either, kept here and nowhere else (D-149).
public interface IStoreUrls
{
    // Null when the store has no proved primary domain. A shop nobody can reach has no absolute address, and
    // inventing one puts a link that goes nowhere into an inbox or a feed somebody else publishes.
    Task<StoreAddress?> FindAsync(CancellationToken cancellationToken);
}

// Where a shop lives and what its pages are called. The language is carried from the start even though it does
// not appear in a path yet: a feed has to declare what language it is written in, and the day a store has two
// of them this becomes a segment here rather than a rewrite everywhere.
public sealed record StoreAddress(string Host, string Language)
{
    // The shapes themselves live in StorePages, because a redirect within one visit has to stay on the host
    // the shopper is already on — moving them to the primary domain would take their cart cookie away from
    // them (D-124) — so the same shapes are needed with and without a host.
    public string Product(string slug) => Page(StorePages.Product(slug));

    public string Category(string slug) => Page(StorePages.Category(slug));

    public string ContentPage(string slug) => Page(StorePages.ContentPage(slug));

    // An image is served by the API rather than the frontend, so its path is ours either way; what this adds
    // is the host, which a feed or an inbox cannot do without.
    public string Image(string path) => Page(path);

    public string Logo => Page("/api/storefront/store/logo");

    public string Home => Page("/");

    private string Page(string path) => $"https://{Host}{(path.StartsWith('/') ? path : "/" + path)}";
}

// What each kind of page is called, without a host. One place, used both for the absolute addresses a feed or
// an inbox needs and for the relative ones a redirect uses (D-149, D-166).
public static class StorePages
{
    public static string Product(string slug) => $"/p/{Escaped(slug)}";

    public static string Category(string slug) => $"/c/{Escaped(slug)}";

    public static string ContentPage(string slug) => $"/pages/{Escaped(slug)}";

    private static string Escaped(string slug) => Uri.EscapeDataString(slug);
}

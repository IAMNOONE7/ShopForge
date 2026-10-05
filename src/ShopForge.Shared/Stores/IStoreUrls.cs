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
    public string Home => Page(string.Empty);

    // The two shapes the storefront serves. Its router has the matching routes and a test on each side says so,
    // because changing one without the other is how a sitemap starts advertising pages that do not exist.
    public string Product(string slug) => Page($"p/{Escaped(slug)}");

    public string Category(string slug) => Page($"c/{Escaped(slug)}");

    public string ContentPage(string slug) => Page($"pages/{Escaped(slug)}");

    // An image is served by the API rather than the frontend, so its path is ours either way; what this adds
    // is the host, which a feed or an inbox cannot do without.
    public string Image(string path) => Page(path.TrimStart('/'));

    public string Logo => Page("api/storefront/store/logo");

    private string Page(string path) => $"https://{Host}/{path}";

    private static string Escaped(string slug) => Uri.EscapeDataString(slug);
}

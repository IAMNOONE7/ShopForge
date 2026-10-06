namespace ShopForge.Shared.Security;

public static class RateLimits
{
    // Signing in and asking for a link: the cheapest things to brute-force, so the tightest window.
    public const string Authentication = "authentication";

    // Everything a caller can change: carts, checkout, reviews, returns, the admin's own writes.
    public const string Writes = "writes";

    // Work that costs the platform real money or time: an import, a rendered document.
    public const string Expensive = "expensive";

    // A crawler reading a sitemap or a feed. It comes back often and in bursts, and it is not writing
    // anything, so sharing a shopper's window would throttle it into failure (D-126, D-167).
    public const string Crawlers = "crawlers";
}

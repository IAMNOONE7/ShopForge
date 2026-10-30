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

    // Somebody typing into a search box, which asks for suggestions on every letter: as often as a crawler, and from
    // a shopper whose own window is for everything else they do (D-227).
    public const string Typing = "typing";

    // A provider telling us what happened: a gateway's push, a mail service's bounce. It comes from a few of the
    // provider's addresses, in bursts, and every one of them retries what it is refused, so it has a window of
    // its own instead of the ceiling every other caller shares (D-196).
    public const string Webhooks = "webhooks";
}

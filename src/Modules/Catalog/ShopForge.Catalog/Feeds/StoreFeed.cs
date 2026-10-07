using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Feeds;

// One shop's arrangement with one shopping engine: whether it is on, the address it is collected from, and how
// the last collection went. A token per store *and* per consumer, so one engine's access can be revoked
// without disturbing the others (D-148).
internal sealed class StoreFeed : IStoreOwned
{
    private StoreFeed()
    {
    }

    public StoreFeed(Guid storeId, string feed, string token)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Feed = feed;
        Token = token;
        IsEnabled = true;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Feed { get; private set; } = null!;

    // Kept as the merchant will read it, because they have to paste it into somebody else's dashboard. It
    // grants a complete price list and nothing else, and rotating it is one call (D-168).
    public string Token { get; private set; } = null!;

    public bool IsEnabled { get; private set; }

    public string? FilePath { get; private set; }

    public DateTimeOffset? LastRunAt { get; private set; }

    public int? LastMilliseconds { get; private set; }

    public long? LastBytes { get; private set; }

    public int? LastProducts { get; private set; }

    // What the last run would not advertise, and why: a shop with half its catalogue missing from a feed needs
    // to know that before the engine tells it.
    public int? LastSkipped { get; private set; }

    public string? LastError { get; private set; }

    public void Switch(bool on) => IsEnabled = on;

    public void Rotate(string token) => Token = token;

    public void Ran(string filePath, DateTimeOffset at, int milliseconds, long bytes, int products, int skipped)
    {
        FilePath = filePath;
        LastRunAt = at;
        LastMilliseconds = milliseconds;
        LastBytes = bytes;
        LastProducts = products;
        LastSkipped = skipped;
        LastError = null;
    }

    // A run that failed leaves the last good document where it is: a shopping engine reading yesterday's
    // prices is better than one reading an error page.
    public void Failed(DateTimeOffset at, string error)
    {
        LastRunAt = at;
        LastError = error.Length <= 500 ? error : error[..500];
    }
}

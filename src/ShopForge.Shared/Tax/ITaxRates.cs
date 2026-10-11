namespace ShopForge.Shared.Tax;

// What a thing attracts where it is going. A store says what its tax classes are and what each destination
// charges for them; everything that works out tax asks here rather than keeping a table of its own (D-187).
public interface ITaxRates
{
    // The codes this store has, so a listing or a delivery method can only name one that exists.
    Task<IReadOnlyList<string>> ClassesAsync(CancellationToken cancellationToken);

    // What each class attracts for a parcel going to this country. The home country is the fallback the
    // caller already knows, so this costs one query: a shop not registered where it is sending charges its
    // own rate, which is what leaving the destination unsaid means.
    Task<DestinationRates> ForAsync(string? destinationCountry, string? homeCountry, CancellationToken cancellationToken);
}

public sealed record DestinationRates(string? Country, IReadOnlyDictionary<string, decimal> ByClass)
{
    public static readonly DestinationRates Nowhere = new(null, new Dictionary<string, decimal>(StringComparer.Ordinal));

    // The rate a thing attracts: what its class charges here, or what the thing itself says when it names no
    // class and when its class says nothing about anywhere relevant.
    public decimal For(string? taxClass, decimal fallback) =>
        taxClass is { Length: > 0 } code && ByClass.TryGetValue(code, out var rate) ? rate : fallback;
}

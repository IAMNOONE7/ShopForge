using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Domain;

// What one country charges for one class. A country nobody has said anything about has no row, and a thing
// going there keeps the rate it already had rather than being taxed at a number nobody chose (D-187).
internal sealed class TaxRate : IStoreOwned
{
    private TaxRate()
    {
    }

    public TaxRate(Guid storeId, string taxClass, string country, decimal rate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taxClass);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        TaxClass = taxClass;
        Country = Countries.Of(country);
        Set(rate);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string TaxClass { get; private set; } = null!;

    public string Country { get; private set; } = null!;

    // A percentage and not an amount of money, so it keeps two decimals whatever the till is in (D-186).
    public decimal Rate { get; private set; }

    public bool Set(decimal rate)
    {
        if (rate is < 0 or > 100 || decimal.Round(rate, 2) != rate)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "A rate is between 0 and 100, with at most two decimals.");
        }

        if (Rate == rate)
        {
            return false;
        }

        Rate = rate;

        return true;
    }
}

internal static class Countries
{
    public static bool IsValid(string? country) =>
        country is { Length: 2 } && country.All(char.IsAsciiLetter);

    public static string Of(string country) =>
        IsValid(country)
            ? country.ToUpperInvariant()
            : throw new ArgumentException($"'{country}' is not a two-letter country code.", nameof(country));
}

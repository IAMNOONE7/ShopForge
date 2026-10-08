namespace ShopForge.Shared.Payments;

// An amount on its own is just a number; what it is in decides how it rounds and what a gateway is told. The
// same 1.005 is 1.01 in euros, 1 in yen and 1.005 in Kuwaiti dinars, so the code and the decimals it has travel
// together and no sum of money is rounded without one (D-186).
public sealed class Currency : IEquatable<Currency>
{
    // ISO 4217 minor units, for the currencies ShopForge can charge in. Selling in one that is not here means
    // adding it, which is the point of the list: a gateway charged in a currency whose decimals nobody checked
    // moves the decimal point on somebody's money.
    private static readonly Dictionary<string, Currency> Known = Build();

    private static readonly decimal[] Factors = [1m, 10m, 100m, 1000m];

    private Currency(string code, int decimals)
    {
        Code = code;
        Decimals = decimals;
    }

    public string Code { get; }

    // How many decimal places the currency has: two for the euro, none for the yen, three for the dinar.
    public int Decimals { get; }

    public static Currency Of(string? code) =>
        Find(code) ?? throw new ArgumentException($"'{code}' is not a currency ShopForge can charge in.", nameof(code));

    public static Currency? Find(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant();

        return normalized is not null && Known.TryGetValue(normalized, out var currency) ? currency : null;
    }

    // Away from zero, which is how a price list is read and how a gateway is charged: half a unit rounds up.
    public decimal Round(decimal amount) => decimal.Round(amount, Decimals, MidpointRounding.AwayFromZero);

    // Towards zero, for a share that must not add up to more than the whole it came out of.
    public decimal RoundDown(decimal amount) => decimal.Round(amount, Decimals, MidpointRounding.ToZero);

    // Whether the currency can express the amount at all: 10.50 is money in euros and is not money in yen.
    public bool Holds(decimal amount) => Round(amount) == amount;

    // What a gateway is told: the amount in the currency's smallest unit, in decimal arithmetic throughout so
    // that a float never comes near it.
    public long ToMinorUnits(decimal amount) =>
        (long)decimal.Round(amount * Factors[Decimals], 0, MidpointRounding.AwayFromZero);

    public static bool operator ==(Currency? left, Currency? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(Currency? left, Currency? right) => !(left == right);

    public bool Equals(Currency? other) => other is not null && other.Code == Code;

    public override bool Equals(object? obj) => Equals(obj as Currency);

    public override int GetHashCode() => Code.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Code;

    private static Dictionary<string, Currency> Build()
    {
        var decimals = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // No minor unit at all: an amount is a whole number of them.
            ["BIF"] = 0,
            ["CLP"] = 0,
            ["DJF"] = 0,
            ["GNF"] = 0,
            ["ISK"] = 0,
            ["JPY"] = 0,
            ["KMF"] = 0,
            ["KRW"] = 0,
            ["PYG"] = 0,
            ["RWF"] = 0,
            ["UGX"] = 0,
            ["VND"] = 0,
            ["VUV"] = 0,
            ["XAF"] = 0,
            ["XOF"] = 0,
            ["XPF"] = 0,

            // Three.
            ["BHD"] = 3,
            ["IQD"] = 3,
            ["JOD"] = 3,
            ["KWD"] = 3,
            ["LYD"] = 3,
            ["OMR"] = 3,
            ["TND"] = 3,

            // Two. The forint belongs here and not above: the fillér is long gone, but ISO 4217 still gives it
            // two decimals and the gateways charge it in them.
            ["AUD"] = 2,
            ["BGN"] = 2,
            ["BRL"] = 2,
            ["CAD"] = 2,
            ["CHF"] = 2,
            ["CNY"] = 2,
            ["CZK"] = 2,
            ["DKK"] = 2,
            ["EUR"] = 2,
            ["GBP"] = 2,
            ["HKD"] = 2,
            ["HUF"] = 2,
            ["ILS"] = 2,
            ["INR"] = 2,
            ["MDL"] = 2,
            ["MXN"] = 2,
            ["NOK"] = 2,
            ["NZD"] = 2,
            ["PLN"] = 2,
            ["RON"] = 2,
            ["RSD"] = 2,
            ["SEK"] = 2,
            ["SGD"] = 2,
            ["TRY"] = 2,
            ["UAH"] = 2,
            ["USD"] = 2,
            ["ZAR"] = 2,
        };

        return decimals.ToDictionary(entry => entry.Key, entry => new Currency(entry.Key, entry.Value), StringComparer.Ordinal);
    }
}

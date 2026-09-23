namespace ShopForge.Orders.Domain;

// One row per store, series and year; the next number is allocated with a single atomic statement (see Numbers).
internal sealed class NumberSequence
{
    public Guid StoreId { get; private set; }

    public string Series { get; private set; } = null!;

    public int Year { get; private set; }

    public int NextNumber { get; private set; }
}

internal static class NumberSeries
{
    public const string Order = "order";
    public const string Invoice = "invoice";
    public const string CreditNote = "credit-note";
}

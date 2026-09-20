namespace ShopForge.Orders.Domain;

// One row per store and year; the next number is allocated with a single atomic statement (see OrderNumbers).
internal sealed class OrderNumberSequence
{
    public Guid StoreId { get; private set; }

    public int Year { get; private set; }

    public int NextNumber { get; private set; }
}

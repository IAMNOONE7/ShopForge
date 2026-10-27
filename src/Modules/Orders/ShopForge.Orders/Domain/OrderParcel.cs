using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

// One parcel the delivery was priced on, estimated when the order was placed (D-217, D-220): what goes in it, what it
// weighs with its packaging, the band of the rate card it fell in, and what it cost. Kept as priced, so a later change
// of settings or prices alters nothing already sold, and kept beside the order rather than inside it, as its returns
// and documents are: loading an order for anything else does not load its parcels. A suggestion booking starts from.
internal sealed class OrderParcel : IStoreOwned
{
    private readonly List<OrderParcelItem> _contents = [];

    private OrderParcel()
    {
    }

    public OrderParcel(Guid storeId, string orderNumber, int number, int grossWeightGrams, int bandUpToGrams, decimal price, bool alone, IEnumerable<OrderParcelItem> contents)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        OrderNumber = orderNumber;
        Number = number;
        GrossWeightGrams = grossWeightGrams;
        BandUpToGrams = bandUpToGrams;
        Price = price;
        Alone = alone;
        _contents.AddRange(contents);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string OrderNumber { get; private set; } = null!;

    public int Number { get; private set; }

    public int GrossWeightGrams { get; private set; }

    public int BandUpToGrams { get; private set; }

    public decimal Price { get; private set; }

    // A form that travels in its own carton, alone in the parcel (D-215).
    public bool Alone { get; private set; }

    public IReadOnlyList<OrderParcelItem> Contents => _contents;
}

internal sealed record OrderParcelItem(Guid VariantId, string Sku, int Quantity);

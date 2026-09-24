using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

internal sealed class Cart : IStoreOwned
{
    public const int MaxQuantity = 99;

    private readonly List<CartLine> _lines = [];

    private Cart()
    {
    }

    public Cart(Guid storeId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public IReadOnlyCollection<CartLine> Lines => _lines;

    // When the shopper last touched it: an untouched cart is cleaned up after a month.
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    // At most one code per cart; applying another replaces it (D-084).
    public string? DiscountCode { get; private set; }

    public void ApplyDiscount(string? code) => DiscountCode = code is null ? null : Discount.Normalize(code);

    public void SetQuantity(Guid storeProductId, int quantity)
    {
        if (quantity is < 0 or > MaxQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), $"Quantity must be between 0 and {MaxQuantity}.");
        }

        var line = _lines.SingleOrDefault(line => line.StoreProductId == storeProductId);

        if (quantity == 0)
        {
            _lines.RemoveAll(candidate => candidate.StoreProductId == storeProductId);
        }
        else if (line is null)
        {
            _lines.Add(new CartLine(storeProductId, quantity));
        }
        else
        {
            line.SetQuantity(quantity);
        }
    }

    public void Add(Guid storeProductId, int quantity)
    {
        var current = _lines.SingleOrDefault(line => line.StoreProductId == storeProductId)?.Quantity ?? 0;

        SetQuantity(storeProductId, Math.Min(current + quantity, MaxQuantity));
    }
}

internal sealed class CartLine
{
    private CartLine()
    {
    }

    internal CartLine(Guid storeProductId, int quantity)
    {
        StoreProductId = storeProductId;
        Quantity = quantity;
    }

    public Guid StoreProductId { get; private set; }

    public int Quantity { get; private set; }

    internal void SetQuantity(int quantity) => Quantity = quantity;
}

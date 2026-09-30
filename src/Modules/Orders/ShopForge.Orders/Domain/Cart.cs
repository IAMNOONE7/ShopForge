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

    // A line is one form of one listing: a medium shirt and a large one are two lines, not one (D-135).
    public void SetQuantity(Guid storeProductId, Guid variantId, int quantity)
    {
        if (quantity is < 0 or > MaxQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), $"Quantity must be between 0 and {MaxQuantity}.");
        }

        var line = Line(storeProductId, variantId);

        if (quantity == 0)
        {
            _lines.RemoveAll(candidate => candidate.StoreProductId == storeProductId && candidate.VariantId == variantId);
        }
        else if (line is null)
        {
            _lines.Add(new CartLine(storeProductId, variantId, quantity));
        }
        else
        {
            line.SetQuantity(quantity);
        }
    }

    public void Add(Guid storeProductId, Guid variantId, int quantity)
    {
        var current = Line(storeProductId, variantId)?.Quantity ?? 0;

        SetQuantity(storeProductId, variantId, Math.Min(current + quantity, MaxQuantity));
    }

    public CartLine? Line(Guid storeProductId, Guid variantId) =>
        _lines.SingleOrDefault(line => line.StoreProductId == storeProductId && line.VariantId == variantId);
}

internal sealed class CartLine
{
    private CartLine()
    {
    }

    internal CartLine(Guid storeProductId, Guid variantId, int quantity)
    {
        StoreProductId = storeProductId;
        VariantId = variantId;
        Quantity = quantity;
    }

    public Guid StoreProductId { get; private set; }

    public Guid VariantId { get; private set; }

    public int Quantity { get; private set; }

    internal void SetQuantity(int quantity) => Quantity = quantity;
}

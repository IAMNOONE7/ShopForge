using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

internal sealed class PaymentMethod : IStoreOwned
{
    private PaymentMethod()
    {
    }

    public PaymentMethod(Guid storeId, string code, string name, string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Code = code;
        Name = name.Trim();
        ProviderKey = providerKey;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string ProviderKey { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public void Update(string name, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        IsActive = isActive;
    }
}

internal sealed class ShippingMethod : IStoreOwned
{
    private ShippingMethod()
    {
    }

    public ShippingMethod(Guid storeId, string code, string name, string providerKey, decimal price, decimal vatRate, bool requiresPickupPoint = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Code = code;
        ProviderKey = providerKey;
        Update(name, price, vatRate, isActive: true, requiresPickupPoint);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string ProviderKey { get; private set; } = null!;

    public decimal Price { get; private set; }

    public decimal VatRate { get; private set; }

    public bool IsActive { get; private set; }

    // The shopper has to choose where the parcel goes before the order can be placed (D-062).
    public bool RequiresPickupPoint { get; private set; }

    public void Update(string name, decimal price, decimal vatRate, bool isActive, bool requiresPickupPoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (price < 0 || decimal.Round(price, 2) != price)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be zero or more with at most two decimals.");
        }

        if (vatRate is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(vatRate), "The VAT rate must be between 0 and 100.");
        }

        Name = name.Trim();
        Price = price;
        VatRate = vatRate;
        IsActive = isActive;
        RequiresPickupPoint = requiresPickupPoint;
    }
}

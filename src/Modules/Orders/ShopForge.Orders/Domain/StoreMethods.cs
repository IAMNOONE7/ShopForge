using ShopForge.Shared.Shipping;
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

    public ShippingMethod(
        Guid storeId,
        string code,
        string name,
        string providerKey,
        decimal price,
        decimal vatRate,
        bool requiresPickupPoint = false,
        int? maxWeightGrams = null,
        IReadOnlyList<string>? countries = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Code = code;
        ProviderKey = providerKey;
        Update(name, price, vatRate, isActive: true, requiresPickupPoint, maxWeightGrams, countries);
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

    // What this method will take, as the store configured it. The price stays flat whatever the parcel weighs
    // (D-145); these decide whether it is offered at all. Null and empty mean the store has set no limit.
    public int? MaxWeightGrams { get; private set; }

    public List<string> Countries { get; private set; } = [];

    public void Update(
        string name,
        decimal price,
        decimal vatRate,
        bool isActive,
        bool requiresPickupPoint,
        int? maxWeightGrams = null,
        IReadOnlyList<string>? countries = null)
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

        if (maxWeightGrams is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWeightGrams), "A weight limit must be more than nothing.");
        }

        Name = name.Trim();
        Price = price;
        VatRate = vatRate;
        IsActive = isActive;
        RequiresPickupPoint = requiresPickupPoint;
        MaxWeightGrams = maxWeightGrams;
        Countries = [.. (countries ?? []).Select(country => country.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    // A method with no limits carries anything, which is every method until a store says otherwise. A method
    // with a weight limit cannot carry a parcel nobody has weighed: an unknown weight is not a light one, and
    // quietly treating it as nothing is how an unliftable parcel reaches a carrier that will refuse it.
    public bool Carries(Parcel parcel) =>
        (MaxWeightGrams is not { } limit || parcel.WeightGrams <= limit)
        && (Countries.Count == 0
            || parcel.DestinationCountry is not { } country
            || Countries.Contains(country.Trim().ToUpperInvariant()));
}

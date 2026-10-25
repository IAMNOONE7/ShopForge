using ShopForge.Orders.Shipping;
using ShopForge.Shared.Payments;
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
        Currency currency,
        bool requiresPickupPoint = false,
        int? maxWeightGrams = null,
        IReadOnlyList<string>? countries = null,
        string? taxClass = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Code = code;
        ProviderKey = providerKey;
        Update(name, price, vatRate, isActive: true, requiresPickupPoint, currency, maxWeightGrams, countries, taxClass);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string ProviderKey { get; private set; } = null!;

    public decimal Price { get; private set; }

    public decimal VatRate { get; private set; }

    // Which of the shop's tax classes a delivery is, or nothing when the rate above applies wherever the
    // parcel goes (D-187).
    public string? TaxClass { get; private set; }

    public bool IsActive { get; private set; }

    // The shopper has to choose where the parcel goes before the order can be placed (D-062).
    public bool RequiresPickupPoint { get; private set; }

    // What this method will take, as the store configured it. The price stays flat whatever the parcel weighs
    // (D-145); these decide whether it is offered at all. Null and empty mean the store has set no limit.
    public int? MaxWeightGrams { get; private set; }

    public List<string> Countries { get; private set; } = [];

    // Flat, one price whatever the cart (D-145), or a price for each parcel the cart is estimated to need (D-218).
    public ShippingPricing Pricing { get; private set; }

    // The carrier's service it sends by: required for a method priced by the parcel, a note for a flat one.
    public string? CarrierServiceKey { get; private set; }

    // What a method priced by the parcel promises within its service, where the store promises less than the carrier
    // takes: a lighter heaviest parcel, a smaller largest one, longest side first.
    public int? ParcelMaxWeightGrams { get; private set; }

    public int? ParcelLongestMm { get; private set; }

    public int? ParcelMiddleMm { get; private set; }

    public int? ParcelShortestMm { get; private set; }

    public Sides? ParcelLargest =>
        ParcelLongestMm is { } longest && ParcelMiddleMm is { } middle && ParcelShortestMm is { } shortest ? new Sides(longest, middle, shortest) : null;

    public void Update(
        string name,
        decimal price,
        decimal vatRate,
        bool isActive,
        bool requiresPickupPoint,
        Currency currency,
        int? maxWeightGrams = null,
        IReadOnlyList<string>? countries = null,
        string? taxClass = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (price < 0 || !currency.Holds(price))
        {
            throw new ArgumentOutOfRangeException(nameof(price), $"Price must be zero or more with at most {currency.Decimals} decimals.");
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
        TaxClass = string.IsNullOrWhiteSpace(taxClass) ? null : taxClass.Trim();
        IsActive = isActive;
        RequiresPickupPoint = requiresPickupPoint;
        MaxWeightGrams = maxWeightGrams;
        Countries = [.. (countries ?? []).Select(country => country.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    public void SendBy(ShippingPricing pricing, string? carrierServiceKey, int? parcelMaxWeightGrams, Sides? parcelLargest)
    {
        if (pricing == ShippingPricing.PerParcel ? carrierServiceKey is null : parcelMaxWeightGrams is not null || parcelLargest is not null)
        {
            throw new ArgumentException("A method priced by the parcel sends by a carrier service, and only such a method limits its parcels.");
        }

        Pricing = pricing;
        CarrierServiceKey = carrierServiceKey;
        ParcelMaxWeightGrams = parcelMaxWeightGrams;
        ParcelLongestMm = parcelLargest?.Longest;
        ParcelMiddleMm = parcelLargest?.Middle;
        ParcelShortestMm = parcelLargest?.Shortest;
    }

    // What the method sends by its service: the service's limits, and the method's own where they are tighter.
    public ServiceLimits LimitsWithin(CarrierService service) =>
        new(
            Math.Min(service.MaxWeightGrams, ParcelMaxWeightGrams ?? int.MaxValue),
            ParcelLargest ?? service.Largest,
            service.MaxSumOfSidesMm,
            ParcelLargest is { } largest ? Math.Min(service.LargestVolumeMm3, largest.Volume) : service.LargestVolumeMm3,
            service.MaxParcels);

    // A method with no limits carries anything, which is every method until a store says otherwise. A method
    // with a weight limit cannot carry a parcel nobody has weighed: an unknown weight is not a light one, and
    // quietly treating it as nothing is how an unliftable parcel reaches a carrier that will refuse it.
    public bool Carries(Parcel parcel) =>
        (MaxWeightGrams is not { } limit || parcel.WeightGrams <= limit)
        && (Countries.Count == 0
            || parcel.DestinationCountry is not { } country
            || Countries.Contains(country.Trim().ToUpperInvariant()));
}

internal enum ShippingPricing
{
    Flat,
    PerParcel,
}

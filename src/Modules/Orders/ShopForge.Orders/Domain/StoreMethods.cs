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

    // What a parcel costs by a method priced by the parcel, band by band on its gross weight, and where those prices
    // came from, so a price list that changes is easy to spot (D-219).
    public List<RateBand> RateBands { get; private set; } = [];

    public RateSource? RatesFrom { get; private set; }

    public DateOnly? RatesPricedOn { get; private set; }

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

        // A flat method has one price and no rate card.
        if (pricing == ShippingPricing.Flat)
        {
            ForgetRates();
        }
    }

    public void PriceParcels(RateSource source, DateOnly? pricedOn, IReadOnlyList<RateBand> bands, Currency currency)
    {
        if (Pricing != ShippingPricing.PerParcel
            || bands.Count == 0
            || bands.Any(band => band.UpToGrams <= 0 || band.Price < 0 || !currency.Holds(band.Price))
            || bands.Zip(bands.Skip(1)).Any(pair => pair.Second.UpToGrams <= pair.First.UpToGrams))
        {
            throw new ArgumentException("A rate card belongs to a method priced by the parcel: at least one band, each heavier than the last, at prices the currency can hold.");
        }

        RateBands = [.. bands];
        RatesFrom = source;
        RatesPricedOn = pricedOn;
    }

    public void ForgetRates()
    {
        RateBands = [];
        RatesFrom = null;
        RatesPricedOn = null;
    }

    // The band a parcel of this gross weight falls in: at a band's bound it is that band, a gram over it the next. Above
    // the heaviest there is none, and a parcel is never charged the top band's price instead (D-219).
    public RateBand? BandFor(int grossWeightGrams) =>
        RateBands.Where(band => grossWeightGrams <= band.UpToGrams).MinBy(band => band.UpToGrams);

    // What the method sends by its service: the service's limits, and the method's own where they are tighter.
    public ServiceLimits LimitsWithin(CarrierService service) =>
        new(
            Math.Min(service.MaxWeightGrams, ParcelMaxWeightGrams ?? int.MaxValue),
            ParcelLargest ?? service.Largest,
            service.MaxSumOfSidesMm,
            ParcelLargest is { } largest ? Math.Min(service.LargestVolumeMm3, largest.Volume) : service.LargestVolumeMm3,
            service.MaxParcels);
}

internal enum ShippingPricing
{
    Flat,
    PerParcel,
}

// One band of a rate card: a parcel up to this gross weight, inclusive, costs this, gross.
internal sealed record RateBand(int UpToGrams, decimal Price);

// The carrier's price list of a date, the shop's own prices, or invented ones that must not reach a shopper (D-219).
internal enum RateSource
{
    Carrier,
    Own,
    Placeholder,
}

using ShopForge.Orders.Shipping;

namespace ShopForge.Orders.Domain;

// A carrier's service as the merchant buys it, "Packeta Z-BOX, Czechia", and what it will take (D-218). Platform data
// with no tenant, kept by the operator and seeded from what the carrier publishes (D-214), so a limit the carrier
// changes is an edit rather than a release. A store's method names one and can only promise less.
internal sealed class CarrierService
{
    public const int MaxKeyLength = 50;
    public const int MaxSourceLength = 500;

    private CarrierService()
    {
    }

    public CarrierService(string key, string providerKey, CarrierServiceKind kind, string country)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        Id = Guid.CreateVersion7();
        Key = key.Trim().ToLowerInvariant();
        ProviderKey = providerKey;
        Kind = kind;
        Country = country.Trim().ToUpperInvariant();
    }

    public Guid Id { get; private set; }

    public string Key { get; private set; } = null!;

    public string ProviderKey { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public CarrierServiceKind Kind { get; private set; }

    public string Country { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public int MaxWeightGrams { get; private set; }

    // The largest parcel, longest side first. A service that limits the sides' sum as well says so beside it.
    public int LongestMm { get; private set; }

    public int MiddleMm { get; private set; }

    public int ShortestMm { get; private set; }

    public int? MaxSumOfSidesMm { get; private set; }

    // The smallest parcel it takes, which is the packaging's concern rather than the estimate's: a smaller thing goes in
    // a box that is not (D-217).
    public int MinLongestMm { get; private set; }

    public int MinMiddleMm { get; private set; }

    public int MinShortestMm { get; private set; }

    // The volume of the largest parcel, which the estimate fills: a locker's largest compartment, or for a rule on the
    // sides the largest box that rule allows.
    public long LargestVolumeMm3 { get; private set; }

    // How many parcels one order may be, where the carrier has a limit of its own.
    public int? MaxParcels { get; private set; }

    // Where the limits were read and when, so any of them can be checked against what the carrier says now.
    public string Source { get; private set; } = null!;

    public DateOnly SourceDate { get; private set; }

    public Sides Largest => new(LongestMm, MiddleMm, ShortestMm);

    public Sides Smallest => new(MinLongestMm, MinMiddleMm, MinShortestMm);

    public bool DeliversToPoint => Kind != CarrierServiceKind.Home;

    public ServiceLimits Limits => new(MaxWeightGrams, Largest, MaxSumOfSidesMm, LargestVolumeMm3, MaxParcels);

    public void Describe(string name, bool isActive, string source, DateOnly sourceDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        Name = name.Trim();
        IsActive = isActive;
        Source = source.Trim();
        SourceDate = sourceDate;
    }

    public void Limit(int maxWeightGrams, Sides largest, int? maxSumOfSidesMm, Sides smallest, long largestVolumeMm3, int? maxParcels)
    {
        if (maxWeightGrams <= 0 || largestVolumeMm3 <= 0 || maxParcels is < 1 || !smallest.FitIn(largest) || largestVolumeMm3 > largest.Volume)
        {
            throw new ArgumentException("A service takes something: a parcel of some weight and some volume, at least as large as its smallest.");
        }

        MaxWeightGrams = maxWeightGrams;
        LongestMm = largest.Longest;
        MiddleMm = largest.Middle;
        ShortestMm = largest.Shortest;
        MaxSumOfSidesMm = maxSumOfSidesMm;
        MinLongestMm = smallest.Longest;
        MinMiddleMm = smallest.Middle;
        MinShortestMm = smallest.Shortest;
        LargestVolumeMm3 = largestVolumeMm3;
        MaxParcels = maxParcels;
    }

    // What a store's method may promise within this service, or why it cannot: never more than the carrier takes, and
    // never a largest parcel the carrier would turn away as too small (D-218). Both numbers are named.
    public string? RefusesWeight(int grams) =>
        grams > MaxWeightGrams ? $"{Name} takes parcels up to {MaxWeightGrams} g; {grams} g is more than that." : null;

    public string? RefusesLargest(Sides sides)
    {
        if (!sides.FitIn(Largest))
        {
            return $"{Name} takes parcels up to {Named(Largest)} mm, longest side first; {Named(sides)} mm is larger than that.";
        }

        if (MaxSumOfSidesMm is { } sum && sides.Sum > sum)
        {
            return $"{Name} takes parcels whose sides add up to at most {sum} mm; these add up to {sides.Sum} mm.";
        }

        return Smallest.FitIn(sides) ? null : $"{Name} takes parcels of at least {Named(Smallest)} mm; {Named(sides)} mm is smaller than that.";
    }

    private static string Named(Sides sides) => $"{sides.Longest} × {sides.Middle} × {sides.Shortest}";
}

internal enum CarrierServiceKind
{
    Locker,
    PickupPoint,
    Home,
}

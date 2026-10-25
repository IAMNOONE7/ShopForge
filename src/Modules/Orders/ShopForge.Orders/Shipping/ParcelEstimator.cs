namespace ShopForge.Orders.Shipping;

// How many parcels a cart needs by one carrier's service and what each weighs, or why that service cannot take it
// (D-217). An estimate, not a packing: every unit is checked alone against the service, a unit that ships alone is a
// parcel of its own, and the rest are shared out heaviest first, each into the first parcel with room for it by
// weight and by volume. Nothing is laid out in three dimensions, and the merchant confirms the real parcels when
// booking (V8).
//
// The same cart gives the same parcels whatever order its lines come in. The work is bounded by counts, never by a
// clock: identical units go in together, and the estimate stops as soon as it would need more parcels than it may.
internal static class ParcelEstimator
{
    // Whatever a store allows, no order is estimated past this many parcels.
    public const int MostParcels = 100;

    public static ParcelEstimate Estimate(IReadOnlyList<ParcelItem> items, ServiceLimits service, ParcelPacking packing)
    {
        // One line per form, however many lines the cart has for it: a form is measured once.
        var lines = items
            .GroupBy(item => item.VariantId)
            .Select(group => group.First() with { Quantity = group.Sum(item => item.Quantity) })
            .OrderBy(item => item.VariantId)
            .ToList();

        // A backstop for forms from before every form had to be weighed and measured (D-216).
        foreach (var line in lines)
        {
            if (line.WeightGrams is not > 0)
            {
                return ParcelEstimate.Refused(ParcelRefusalReason.MissingWeight, line.VariantId);
            }

            if (line.LengthMm is null || line.WidthMm is null || line.HeightMm is null)
            {
                return ParcelEstimate.Refused(ParcelRefusalReason.MissingSize, line.VariantId);
            }
        }

        var units = lines
            .Select(line => new Unit(line.VariantId, line.Quantity, line.WeightGrams!.Value, Sides.Of(line.LengthMm!.Value, line.WidthMm!.Value, line.HeightMm!.Value), line.ShipsAlone))
            .OrderByDescending(unit => unit.WeightGrams)
            .ThenByDescending(unit => unit.Sides.Volume)
            .ThenBy(unit => unit.VariantId)
            .ToList();

        foreach (var unit in units)
        {
            if (!unit.Sides.FitIn(service.LargestParcel) || (service.MaxSumOfSidesMm is { } sum && unit.Sides.Sum > sum))
            {
                return ParcelEstimate.Refused(ParcelRefusalReason.ItemTooLarge, unit.VariantId);
            }

            // A unit that ships alone travels as it is; anything else goes in packaging, which Packeta weighs too.
            if (unit.WeightGrams + (unit.ShipsAlone ? 0 : packing.WeightGrams) > service.MaxWeightGrams)
            {
                return ParcelEstimate.Refused(ParcelRefusalReason.ItemTooHeavy, unit.VariantId);
            }
        }

        var most = Math.Min(MostParcels, Math.Min(packing.MostParcels, service.MaxParcels ?? int.MaxValue));
        var parcels = new List<Packed>();

        foreach (var unit in units.Where(unit => unit.ShipsAlone))
        {
            for (var placed = 0; placed < unit.Quantity; placed++)
            {
                if (parcels.Count == most)
                {
                    return ParcelEstimate.Refused(ParcelRefusalReason.TooManyParcels, null);
                }

                parcels.Add(Packed.Alone(unit));
            }
        }

        var shared = new List<Packed>();
        long weightRoom = service.MaxWeightGrams - packing.WeightGrams;
        var volumeRoom = service.LargestVolumeMm3 * packing.FillPercent / 100;

        foreach (var unit in units.Where(unit => !unit.ShipsAlone))
        {
            var remaining = unit.Quantity;

            foreach (var parcel in shared)
            {
                var fits = Math.Min(remaining, Math.Min((weightRoom - parcel.WeightGrams) / unit.WeightGrams, (volumeRoom - parcel.VolumeMm3) / unit.Sides.Volume));

                if (fits > 0)
                {
                    parcel.Add(unit, (int)fits);
                    remaining -= (int)fits;
                }

                if (remaining == 0)
                {
                    break;
                }
            }

            while (remaining > 0)
            {
                if (parcels.Count + shared.Count == most)
                {
                    return ParcelEstimate.Refused(ParcelRefusalReason.TooManyParcels, null);
                }

                // A new parcel takes as many as it has room for, and always takes one: a unit with more volume than a
                // parcel may be filled with still fits the parcel alone, which was checked above.
                var fits = Math.Max(1, Math.Min(remaining, Math.Min(weightRoom / unit.WeightGrams, volumeRoom / unit.Sides.Volume)));
                var parcel = new Packed(alone: false);
                parcel.Add(unit, (int)fits);
                shared.Add(parcel);
                remaining -= (int)fits;
            }
        }

        return new ParcelEstimate(
            [
                .. parcels.Concat(shared).Select(parcel => new EstimatedParcel(
                    parcel.Contents,
                    (int)parcel.WeightGrams + (parcel.IsAlone ? 0 : packing.WeightGrams),
                    parcel.IsAlone)),
            ],
            null);
    }

    private sealed record Unit(Guid VariantId, int Quantity, int WeightGrams, Sides Sides, bool ShipsAlone);

    private sealed class Packed(bool alone)
    {
        private readonly List<ParcelContent> _contents = [];

        public bool IsAlone { get; } = alone;

        public long WeightGrams { get; private set; }

        public long VolumeMm3 { get; private set; }

        public IReadOnlyList<ParcelContent> Contents => _contents;

        public static Packed Alone(Unit unit)
        {
            var parcel = new Packed(alone: true);
            parcel.Add(unit, 1);

            return parcel;
        }

        public void Add(Unit unit, int quantity)
        {
            _contents.Add(new ParcelContent(unit.VariantId, quantity));
            WeightGrams += (long)unit.WeightGrams * quantity;
            VolumeMm3 += unit.Sides.Volume * quantity;
        }
    }
}

// A form in the cart and how many of it, measured as the catalogue has it (D-215).
internal sealed record ParcelItem(Guid VariantId, int Quantity, int? WeightGrams, int? LengthMm, int? WidthMm, int? HeightMm, bool ShipsAlone);

// What one carrier's service takes (V28d keeps these per service): the heaviest parcel, the largest one, what its
// sides may add up to, the volume of the largest parcel it takes, which the estimate fills, and how many parcels one
// order may be where the carrier says.
internal sealed record ServiceLimits(int MaxWeightGrams, Sides LargestParcel, int? MaxSumOfSidesMm, long LargestVolumeMm3, int? MaxParcels);

// The store's own say (V28d): what its packaging adds to each parcel it packs, how full of goods a parcel may be, as
// a share of the largest parcel's volume, and the largest order it sends by parcel.
internal sealed record ParcelPacking(int WeightGrams, int FillPercent, int MostParcels);

// Three sides, longest first, so two sizes compare side by side whichever way round each was measured: a box fits in
// another, turned as it needs to be, exactly when each of its sides is no longer than the other's.
internal readonly record struct Sides(int Longest, int Middle, int Shortest)
{
    public long Volume => (long)Longest * Middle * Shortest;

    public int Sum => Longest + Middle + Shortest;

    public static Sides Of(int a, int b, int c)
    {
        int[] sides = [a, b, c];
        Array.Sort(sides);

        return new Sides(sides[2], sides[1], sides[0]);
    }

    public bool FitIn(Sides limit) => Longest <= limit.Longest && Middle <= limit.Middle && Shortest <= limit.Shortest;
}

internal sealed record ParcelEstimate(IReadOnlyList<EstimatedParcel> Parcels, ParcelRefusal? Refusal)
{
    public static ParcelEstimate Refused(ParcelRefusalReason reason, Guid? variantId) => new([], new ParcelRefusal(reason, variantId));
}

// A parcel's contents and what it weighs when weighed: its goods, and the packaging unless it travels as it is.
internal sealed record EstimatedParcel(IReadOnlyList<ParcelContent> Contents, int GrossWeightGrams, bool Alone);

internal sealed record ParcelContent(Guid VariantId, int Quantity);

// Why a service cannot take the cart, naming the form where one is to blame.
internal sealed record ParcelRefusal(ParcelRefusalReason Reason, Guid? VariantId);

internal enum ParcelRefusalReason
{
    MissingWeight,
    MissingSize,
    ItemTooLarge,
    ItemTooHeavy,
    TooManyParcels,
}

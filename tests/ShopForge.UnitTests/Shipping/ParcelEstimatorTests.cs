using System.Diagnostics;
using ShopForge.Orders.Shipping;

namespace ShopForge.UnitTests.Shipping;

// How many parcels a cart needs by one service, estimated rather than packed (D-217). The limits are shaped like
// Packeta's (D-214): a Z-BOX takes 15 kg in a compartment up to 60 × 43 × 35 cm, one parcel to an order until Packeta
// says otherwise; a Czech doorstep takes 5 kg with sides adding up to 120 cm, the longest 70 cm.
public sealed class ParcelEstimatorTests
{
    private static readonly ServiceLimits ZBox = new(15_000, Sides.Of(600, 430, 350), MaxSumOfSidesMm: null, 600L * 430 * 350, MaxParcels: 1);
    private static readonly ServiceLimits Doorstep = new(5_000, Sides.Of(700, 700, 700), MaxSumOfSidesMm: 1_200, 400L * 400 * 400, MaxParcels: null);
    private static readonly ParcelPacking Packing = new(WeightGrams: 300, FillPercent: 70, MostParcels: 10);

    [Fact]
    public void One_small_item_is_one_parcel_weighed_with_its_packaging()
    {
        var screwdriver = Item(weight: 120, 250, 40, 30);

        var estimate = ParcelEstimator.Estimate([screwdriver], ZBox, Packing);

        var parcel = Assert.Single(estimate.Parcels);
        Assert.Null(estimate.Refusal);
        Assert.Equal([new ParcelContent(screwdriver.VariantId, 1)], parcel.Contents);
        Assert.Equal(420, parcel.GrossWeightGrams);
        Assert.False(parcel.Alone);
    }

    [Fact]
    public void Five_small_tools_go_in_one_parcel_heaviest_first()
    {
        ParcelItem[] tools = [Item(150, 200, 40, 30), Item(350, 300, 60, 40), Item(250, 220, 50, 30), Item(200, 180, 40, 20), Item(300, 260, 50, 40)];

        var estimate = ParcelEstimator.Estimate(tools, Doorstep, Packing);

        var parcel = Assert.Single(estimate.Parcels);
        Assert.Equal(
            tools.OrderByDescending(tool => tool.WeightGrams).Select(tool => new ParcelContent(tool.VariantId, 1)),
            parcel.Contents);
        Assert.Equal(1_250 + 300, parcel.GrossWeightGrams);
    }

    // A boxed drill travels in its own carton, at its own weight, and shares with nothing.
    [Fact]
    public void A_tool_that_ships_alone_is_a_parcel_of_its_own_at_its_own_weight()
    {
        var drill = Item(2_600, 420, 330, 110, shipsAlone: true);
        var bits = Item(80, 120, 60, 20, quantity: 2);

        var estimate = ParcelEstimator.Estimate([bits, drill], Doorstep, Packing);

        Assert.Equal(2, estimate.Parcels.Count);
        Assert.Equal((true, 2_600), (estimate.Parcels[0].Alone, estimate.Parcels[0].GrossWeightGrams));
        Assert.Equal([new ParcelContent(drill.VariantId, 1)], estimate.Parcels[0].Contents);
        Assert.Equal((false, 160 + 300), (estimate.Parcels[1].Alone, estimate.Parcels[1].GrossWeightGrams));
        Assert.Equal([new ParcelContent(bits.VariantId, 2)], estimate.Parcels[1].Contents);
    }

    // 4.5 kg in 300 g of packaging leaves 200 g of a 5 kg parcel, and the next thing weighs more than that.
    [Fact]
    public void A_heavy_tool_leaves_no_room_for_anything_else_by_weight()
    {
        var anvil = Item(4_500, 300, 150, 120);
        var file = Item(300, 250, 30, 10);

        var estimate = ParcelEstimator.Estimate([file, anvil], Doorstep, Packing);

        Assert.Equal([4_800, 600], estimate.Parcels.Select(parcel => parcel.GrossWeightGrams));
        Assert.Equal([anvil.VariantId, file.VariantId], estimate.Parcels.Select(parcel => parcel.Contents.Single().VariantId));
    }

    [Theory]
    [InlineData(700, 200, 100)]
    [InlineData(440, 440, 100)]
    [InlineData(600, 430, 351)]
    public void An_item_too_large_for_a_z_box_takes_the_z_box_off_the_list(int length, int width, int height)
    {
        var item = Item(1_000, length, width, height);

        var estimate = ParcelEstimator.Estimate([Item(100, 100, 100, 100), item], ZBox, Packing);

        Assert.Empty(estimate.Parcels);
        Assert.Equal(new ParcelRefusal(ParcelRefusalReason.ItemTooLarge, item.VariantId), estimate.Refusal);
    }

    // Sides are compared longest first, so a box measured any way round fits a compartment it fits turned.
    [Theory]
    [InlineData(350, 600, 430)]
    [InlineData(430, 350, 600)]
    [InlineData(600, 430, 350)]
    public void An_item_fits_whichever_way_round_it_was_measured(int length, int width, int height)
    {
        var estimate = ParcelEstimator.Estimate([Item(1_000, length, width, height)], ZBox, Packing);

        Assert.Null(estimate.Refusal);
        Assert.Single(estimate.Parcels);
    }

    [Fact]
    public void The_sides_may_add_up_to_no_more_than_the_service_allows()
    {
        var justSo = Item(1_000, 600, 400, 200);
        var tooRound = Item(1_000, 600, 400, 201);

        Assert.Null(ParcelEstimator.Estimate([justSo], Doorstep, Packing).Refusal);
        Assert.Equal(new ParcelRefusal(ParcelRefusalReason.ItemTooLarge, tooRound.VariantId), ParcelEstimator.Estimate([tooRound], Doorstep, Packing).Refusal);
    }

    // Packeta weighs the parcel, packaging and all; a unit that ships alone has no packaging of ours to add.
    [Fact]
    public void A_unit_is_too_heavy_when_it_and_its_packaging_weigh_more_than_the_service_takes()
    {
        var atTheLimit = Item(4_700, 300, 200, 100);
        var overIt = Item(4_701, 300, 200, 100);
        var aloneOverIt = Item(4_701, 300, 200, 100, shipsAlone: true);
        var aloneTooHeavy = Item(5_001, 300, 200, 100, shipsAlone: true);

        Assert.Equal(5_000, Assert.Single(ParcelEstimator.Estimate([atTheLimit], Doorstep, Packing).Parcels).GrossWeightGrams);
        Assert.Equal(new ParcelRefusal(ParcelRefusalReason.ItemTooHeavy, overIt.VariantId), ParcelEstimator.Estimate([overIt], Doorstep, Packing).Refusal);
        Assert.Equal(4_701, Assert.Single(ParcelEstimator.Estimate([aloneOverIt], Doorstep, Packing).Parcels).GrossWeightGrams);
        Assert.Equal(new ParcelRefusal(ParcelRefusalReason.ItemTooHeavy, aloneTooHeavy.VariantId), ParcelEstimator.Estimate([aloneTooHeavy], Doorstep, Packing).Refusal);
    }

    // Three cushions weigh 1.2 kg, which one parcel carries easily, but a doorstep parcel filled to 70 % of 64 litres
    // holds two of them at 18 litres each, not three.
    [Fact]
    public void A_bulky_light_cart_is_split_by_volume_rather_than_weight()
    {
        var cushion = Item(400, 300, 300, 200, quantity: 3);

        var estimate = ParcelEstimator.Estimate([cushion], Doorstep, Packing);

        Assert.Equal([2, 1], estimate.Parcels.Select(parcel => parcel.Contents.Single().Quantity));
        Assert.Equal([800 + 300, 400 + 300], estimate.Parcels.Select(parcel => parcel.GrossWeightGrams));
    }

    // Something that fits the parcel but fills more of it than goods may still goes, alone in it.
    [Fact]
    public void A_unit_larger_than_a_parcel_may_be_filled_with_still_goes_alone()
    {
        var crate = Item(2_000, 590, 420, 340, quantity: 2);

        var estimate = ParcelEstimator.Estimate([crate], ZBox with { MaxParcels = null }, Packing);

        Assert.Equal([1, 1], estimate.Parcels.Select(parcel => parcel.Contents.Single().Quantity));
    }

    [Fact]
    public void Ninety_nine_of_one_thing_are_counted_together()
    {
        var washer = Item(100, 30, 30, 3, quantity: 99);

        var estimate = ParcelEstimator.Estimate([washer], Doorstep, Packing);

        Assert.Equal([47, 47, 5], estimate.Parcels.Select(parcel => parcel.Contents.Single().Quantity));
        Assert.Equal([5_000, 5_000, 800], estimate.Parcels.Select(parcel => parcel.GrossWeightGrams));
    }

    [Fact]
    public void One_parcel_more_than_the_store_allows_is_refused()
    {
        var cushion = Item(400, 300, 300, 200, quantity: 5);

        var allowed = ParcelEstimator.Estimate([cushion], Doorstep, Packing with { MostParcels = 3 });
        var refused = ParcelEstimator.Estimate([cushion], Doorstep, Packing with { MostParcels = 2 });

        Assert.Equal(3, allowed.Parcels.Count);
        Assert.Equal(new ParcelRefusal(ParcelRefusalReason.TooManyParcels, null), refused.Refusal);
        Assert.Empty(refused.Parcels);
    }

    // Until Packeta says several packets may go to one box, a Z-BOX order is one parcel (D-214, A20).
    [Fact]
    public void A_service_that_takes_one_parcel_refuses_a_cart_that_needs_two()
    {
        var oneEach = ParcelEstimator.Estimate([Item(8_000, 300, 200, 100)], ZBox, Packing);
        var twoNeeded = ParcelEstimator.Estimate([Item(8_000, 300, 200, 100, quantity: 2)], ZBox, Packing);
        var aloneTwice = ParcelEstimator.Estimate([Item(1_000, 300, 200, 100, quantity: 2, shipsAlone: true)], ZBox, Packing);

        Assert.Single(oneEach.Parcels);
        Assert.Equal(ParcelRefusalReason.TooManyParcels, twoNeeded.Refusal!.Reason);
        Assert.Equal(ParcelRefusalReason.TooManyParcels, aloneTwice.Refusal!.Reason);
    }

    // However much a store allows, an order is never estimated past a hundred parcels.
    [Fact]
    public void No_order_is_estimated_past_a_hundred_parcels()
    {
        var unbounded = Doorstep with { MaxParcels = null };
        var bricks = Item(4_000, 200, 100, 60, quantity: 100);

        var hundred = ParcelEstimator.Estimate([bricks], unbounded, Packing with { MostParcels = 1_000 });
        var more = ParcelEstimator.Estimate([bricks with { Quantity = 101 }], unbounded, Packing with { MostParcels = 1_000 });

        Assert.Equal(100, hundred.Parcels.Count);
        Assert.Equal(ParcelRefusalReason.TooManyParcels, more.Refusal!.Reason);
    }

    // A backstop for forms from before every form had to be weighed and measured (D-216): refused, never guessed.
    [Fact]
    public void A_form_without_a_weight_or_its_sizes_is_refused_rather_than_guessed()
    {
        var weightless = Item(1_000, 100, 100, 100) with { WeightGrams = 0 };
        var unweighed = Item(1_000, 100, 100, 100) with { WeightGrams = null };
        var unsized = Item(1_000, 100, 100, 100) with { HeightMm = null };

        Assert.Equal(new ParcelRefusal(ParcelRefusalReason.MissingWeight, weightless.VariantId), ParcelEstimator.Estimate([weightless], Doorstep, Packing).Refusal);
        Assert.Equal(new ParcelRefusal(ParcelRefusalReason.MissingWeight, unweighed.VariantId), ParcelEstimator.Estimate([unweighed], Doorstep, Packing).Refusal);
        Assert.Equal(new ParcelRefusal(ParcelRefusalReason.MissingSize, unsized.VariantId), ParcelEstimator.Estimate([unsized], Doorstep, Packing).Refusal);
    }

    // When more than one form is to blame, the same one is named whatever order the cart is in.
    [Fact]
    public void A_refusal_names_the_same_form_however_the_cart_is_ordered()
    {
        var first = Item(1_000, 100, 100, 100) with { VariantId = Guid.Parse("00000000-0000-0000-0000-000000000001"), WeightGrams = null };
        var second = first with { VariantId = Guid.Parse("00000000-0000-0000-0000-000000000002") };

        Assert.Equal(first.VariantId, ParcelEstimator.Estimate([first, second], Doorstep, Packing).Refusal!.VariantId);
        Assert.Equal(first.VariantId, ParcelEstimator.Estimate([second, first], Doorstep, Packing).Refusal!.VariantId);
    }

    [Fact]
    public void An_empty_cart_needs_no_parcels()
    {
        var estimate = ParcelEstimator.Estimate([], ZBox, Packing);

        Assert.Empty(estimate.Parcels);
        Assert.Null(estimate.Refusal);
    }

    // The invariants, on generated carts, services and packaging, reproducible from the seed: every unit in exactly
    // one parcel, every parcel within its weight and its share of volume, nothing placed that the service cannot take
    // alone, and a refusal for too many parcels only when more were needed.
    [Fact]
    public void On_every_generated_cart_each_unit_is_in_one_parcel_within_the_limits()
    {
        var random = new Random(20261025);
        var estimated = 0;

        for (var attempt = 0; attempt < 1_000; attempt++)
        {
            var (items, service, packing) = Generated(random);
            var estimate = ParcelEstimator.Estimate(items, service, packing);
            var unfit = items.Where(item => !FitsAlone(item, service, packing)).Select(item => item.VariantId).ToHashSet();

            if (estimate.Refusal is { } refusal)
            {
                switch (refusal.Reason)
                {
                    case ParcelRefusalReason.ItemTooLarge or ParcelRefusalReason.ItemTooHeavy:
                        Assert.Contains(refusal.VariantId!.Value, unfit);
                        break;
                    case ParcelRefusalReason.TooManyParcels:
                        Assert.Empty(unfit);
                        var relaxed = ParcelEstimator.Estimate(items, service with { MaxParcels = null }, packing with { MostParcels = ParcelEstimator.MostParcels });
                        Assert.True(relaxed.Refusal is not null || relaxed.Parcels.Count > Most(service, packing), $"attempt {attempt}");
                        break;
                    default:
                        Assert.Fail($"attempt {attempt}: {refusal}");
                        break;
                }

                continue;
            }

            estimated++;
            Assert.Empty(unfit);
            Assert.True(estimate.Parcels.Count <= Most(service, packing), $"attempt {attempt}");
            Assert.Equal(
                items.GroupBy(item => item.VariantId).Select(group => (group.Key, group.Sum(item => item.Quantity))).Order(),
                estimate.Parcels.SelectMany(parcel => parcel.Contents).GroupBy(content => content.VariantId).Select(group => (group.Key, group.Sum(content => content.Quantity))).Order());

            var byId = items.GroupBy(item => item.VariantId).ToDictionary(group => group.Key, group => group.First());
            var volumeRoom = service.LargestVolumeMm3 * packing.FillPercent / 100;

            foreach (var parcel in estimate.Parcels)
            {
                var units = parcel.Contents.Sum(content => content.Quantity);
                var goods = parcel.Contents.Sum(content => (long)byId[content.VariantId].WeightGrams!.Value * content.Quantity);
                var volume = parcel.Contents.Sum(content => SidesOf(byId[content.VariantId]).Volume * content.Quantity);

                Assert.True(parcel.GrossWeightGrams <= service.MaxWeightGrams, $"attempt {attempt}");
                Assert.All(parcel.Contents, content => Assert.Equal(parcel.Alone, byId[content.VariantId].ShipsAlone));

                if (parcel.Alone)
                {
                    Assert.Equal(1, units);
                    Assert.Equal(goods, parcel.GrossWeightGrams);
                }
                else
                {
                    Assert.Equal(goods + packing.WeightGrams, parcel.GrossWeightGrams);
                    Assert.True(volume <= volumeRoom || units == 1, $"attempt {attempt}");
                }
            }
        }

        Assert.True(estimated >= 200, $"only {estimated} of the generated carts were estimated rather than refused");
    }

    // The same cart in any order, its lines shuffled or a line split in two, is the same estimate.
    [Fact]
    public void The_estimate_is_the_same_however_the_cart_is_ordered()
    {
        var random = new Random(20261026);

        for (var attempt = 0; attempt < 500; attempt++)
        {
            var (items, service, packing) = Generated(random);
            var reordered = items.OrderBy(_ => random.Next()).ToList();
            var split = reordered.FindIndex(item => item.Quantity > 1);

            if (split >= 0)
            {
                var item = reordered[split];
                var part = random.Next(1, item.Quantity);
                reordered[split] = item with { Quantity = part };
                reordered.Add(item with { Quantity = item.Quantity - part });
            }

            Assert.Equal(Describe(ParcelEstimator.Estimate(items, service, packing)), Describe(ParcelEstimator.Estimate(reordered, service, packing)));
        }
    }

    // A hundred lines of ninety-nine, the most a cart line holds, estimated well inside the time a page can wait.
    [Fact]
    public void A_cart_of_a_hundred_lines_of_ninety_nine_is_estimated_within_fifty_milliseconds()
    {
        var random = new Random(100_99);
        var items = Enumerable.Range(0, 100)
            .Select(_ => new ParcelItem(GuidFrom(random), 99, random.Next(10, 60), random.Next(50, 150), random.Next(30, 80), random.Next(10, 40), ShipsAlone: false))
            .ToList();
        var service = ZBox with { MaxParcels = null };
        var packing = Packing with { MostParcels = ParcelEstimator.MostParcels };

        // The first call pays for compiling the code, which no shopper's request does.
        ParcelEstimator.Estimate(items, service, packing);
        var clock = Stopwatch.StartNew();
        var estimate = ParcelEstimator.Estimate(items, service, packing);
        clock.Stop();

        Assert.Null(estimate.Refusal);
        Assert.Equal(9_900, estimate.Parcels.Sum(parcel => parcel.Contents.Sum(content => content.Quantity)));
        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(50), $"{clock.Elapsed.TotalMilliseconds:F1} ms");
    }

    private static ParcelItem Item(int weight, int length, int width, int height, int quantity = 1, bool shipsAlone = false) =>
        new(Guid.NewGuid(), quantity, weight, length, width, height, shipsAlone);

    private static Sides SidesOf(ParcelItem item) => Sides.Of(item.LengthMm!.Value, item.WidthMm!.Value, item.HeightMm!.Value);

    private static bool FitsAlone(ParcelItem item, ServiceLimits service, ParcelPacking packing) =>
        SidesOf(item).FitIn(service.LargestParcel)
        && (service.MaxSumOfSidesMm is not { } sum || SidesOf(item).Sum <= sum)
        && item.WeightGrams + (item.ShipsAlone ? 0 : packing.WeightGrams) <= service.MaxWeightGrams;

    private static int Most(ServiceLimits service, ParcelPacking packing) =>
        Math.Min(ParcelEstimator.MostParcels, Math.Min(packing.MostParcels, service.MaxParcels ?? int.MaxValue));

    private static string Describe(ParcelEstimate estimate) =>
        estimate.Refusal is { } refusal
            ? $"refused {refusal.Reason} {refusal.VariantId}"
            : string.Join(" | ", estimate.Parcels.Select(parcel =>
                $"{parcel.GrossWeightGrams} g{(parcel.Alone ? " alone" : string.Empty)}: {string.Join(", ", parcel.Contents.Select(content => $"{content.Quantity}× {content.VariantId}"))}"));

    // Mostly the small and middling things a tool shop sends, now and then something too big or too heavy for the
    // service, and services and packaging from tight to generous.
    private static (List<ParcelItem> Items, ServiceLimits Service, ParcelPacking Packing) Generated(Random random)
    {
        var largest = Sides.Of(random.Next(200, 801), random.Next(200, 801), random.Next(100, 801));
        var service = new ServiceLimits(
            random.Next(2_000, 30_001),
            largest,
            random.Next(2) == 0 ? null : random.Next(600, 2_401),
            largest.Volume * random.Next(50, 101) / 100,
            random.Next(5) switch { 0 => 1, 1 => random.Next(2, 20), _ => null });
        var packing = new ParcelPacking(random.Next(0, 501), random.Next(30, 101), random.Next(1, 101));
        var items = Enumerable.Range(0, random.Next(1, 9))
            .Select(_ => new ParcelItem(
                GuidFrom(random),
                random.Next(1, 30),
                random.Next(20) == 0 ? random.Next(1, 40_000) : random.Next(1, 3_000),
                random.Next(20) == 0 ? random.Next(1, 1_500) : random.Next(10, 400),
                random.Next(10, 400),
                random.Next(5, 300),
                random.Next(10) == 0))
            .ToList();

        return (items, service, packing);
    }

    private static Guid GuidFrom(Random random)
    {
        var bytes = new byte[16];
        random.NextBytes(bytes);

        return new Guid(bytes);
    }
}

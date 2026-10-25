using ShopForge.Orders.Domain;
using ShopForge.Orders.Shipping;
using ShopForge.Shared.Payments;

namespace ShopForge.UnitTests.Shipping;

// What a carrier's service takes, and what a store's method may promise within it: never more, and never a parcel the
// carrier would turn away as too small (D-218). The services are shaped like the two seeded from Packeta (D-214).
public sealed class CarrierServiceTests
{
    [Fact]
    public void A_method_may_promise_no_heavier_parcel_than_the_service_takes()
    {
        var zbox = ZBox();

        Assert.Null(zbox.RefusesWeight(15_000));
        Assert.Equal("Packeta Z-BOX, Czechia takes parcels up to 15000 g; 15001 g is more than that.", zbox.RefusesWeight(15_001));
    }

    // Longest side first, so a box measured any way round is compared as it would be turned to fit.
    [Theory]
    [InlineData(350, 600, 430, null)]
    [InlineData(601, 400, 300, "Packeta Z-BOX, Czechia takes parcels up to 600 × 430 × 350 mm, longest side first; 601 × 400 × 300 mm is larger than that.")]
    [InlineData(440, 100, 440, "Packeta Z-BOX, Czechia takes parcels up to 600 × 430 × 350 mm, longest side first; 440 × 440 × 100 mm is larger than that.")]
    [InlineData(500, 400, 351, "Packeta Z-BOX, Czechia takes parcels up to 600 × 430 × 350 mm, longest side first; 500 × 400 × 351 mm is larger than that.")]
    public void A_largest_parcel_is_compared_side_by_side_longest_first(int length, int width, int height, string? refusal)
    {
        Assert.Equal(refusal, ZBox().RefusesLargest(Sides.Of(length, width, height)));
    }

    [Fact]
    public void A_largest_parcel_s_sides_add_up_to_no_more_than_the_service_allows()
    {
        var doorstep = Doorstep();

        Assert.Null(doorstep.RefusesLargest(Sides.Of(600, 400, 200)));
        Assert.Equal(
            "Packeta home delivery, Czechia takes parcels whose sides add up to at most 1200 mm; these add up to 1201 mm.",
            doorstep.RefusesLargest(Sides.Of(600, 400, 201)));
    }

    // A store's biggest box smaller than the smallest parcel the carrier takes would leave nothing it could send.
    [Fact]
    public void A_largest_parcel_smaller_than_the_smallest_the_service_takes_is_refused()
    {
        var zbox = ZBox();

        Assert.Null(zbox.RefusesLargest(Sides.Of(100, 70, 10)));
        Assert.Equal(
            "Packeta Z-BOX, Czechia takes parcels of at least 100 × 70 × 10 mm; 100 × 69 × 10 mm is smaller than that.",
            zbox.RefusesLargest(Sides.Of(100, 69, 10)));
    }

    [Fact]
    public void A_method_sends_by_the_service_s_limits_where_it_sets_none_of_its_own()
    {
        var zbox = ZBox();

        Assert.Equal(new ServiceLimits(15_000, Sides.Of(600, 430, 350), null, 600L * 430 * 350, 1), Method(zbox).LimitsWithin(zbox));
        Assert.Equal(zbox.Limits, Method(zbox).LimitsWithin(zbox));
    }

    // A lighter heaviest parcel, and a smaller largest one whose volume becomes the one the estimate fills.
    [Fact]
    public void A_method_s_own_tighter_limits_are_the_ones_it_sends_by()
    {
        var doorstep = Doorstep();
        var method = Method(doorstep);

        method.SendBy(ShippingPricing.PerParcel, doorstep.Key, parcelMaxWeightGrams: 3_000, Sides.Of(300, 400, 300));
        var lighter = method.LimitsWithin(doorstep);
        method.SendBy(ShippingPricing.PerParcel, doorstep.Key, parcelMaxWeightGrams: 6_000, Sides.Of(700, 300, 100));
        var roomier = method.LimitsWithin(doorstep);

        Assert.Equal(new ServiceLimits(3_000, Sides.Of(400, 300, 300), 1_200, 400L * 300 * 300, null), lighter);
        Assert.Equal((5_000, 700L * 300 * 100), (roomier.MaxWeightGrams, roomier.LargestVolumeMm3));
    }

    // A store's box can be no roomier than the service's own largest parcel, whatever its sides.
    [Fact]
    public void A_method_s_box_never_holds_more_than_the_service_s_largest_parcel()
    {
        var tight = Service("tight", "Tight", CarrierServiceKind.Home, 5_000, Sides.Of(700, 700, 700), 1_200, 30_000_000, null);
        var method = Method(tight);

        method.SendBy(ShippingPricing.PerParcel, tight.Key, null, Sides.Of(400, 300, 300));

        Assert.Equal(30_000_000, method.LimitsWithin(tight).LargestVolumeMm3);
    }

    [Theory]
    [InlineData(nameof(CarrierServiceKind.Locker), true)]
    [InlineData(nameof(CarrierServiceKind.PickupPoint), true)]
    [InlineData(nameof(CarrierServiceKind.Home), false)]
    public void A_service_delivers_to_a_point_unless_it_goes_to_the_door(string kind, bool toAPoint)
    {
        Assert.Equal(toAPoint, new CarrierService("any", "packeta", Enum.Parse<CarrierServiceKind>(kind), "CZ").DeliversToPoint);
    }

    [Fact]
    public void A_method_priced_by_the_parcel_sends_by_a_service_and_only_it_limits_parcels()
    {
        var method = Method(ZBox());

        Assert.Throws<ArgumentException>(() => method.SendBy(ShippingPricing.PerParcel, null, null, null));
        Assert.Throws<ArgumentException>(() => method.SendBy(ShippingPricing.Flat, null, 1_000, null));
        Assert.Throws<ArgumentException>(() => method.SendBy(ShippingPricing.Flat, null, null, Sides.Of(100, 100, 100)));
    }

    [Fact]
    public void A_service_takes_a_parcel_of_some_weight_and_volume_no_smaller_than_its_smallest()
    {
        var service = ZBox();

        Assert.Throws<ArgumentException>(() => service.Limit(0, Sides.Of(600, 430, 350), null, Sides.Of(100, 70, 10), 1_000, null));
        Assert.Throws<ArgumentException>(() => service.Limit(15_000, Sides.Of(600, 430, 350), null, Sides.Of(100, 70, 10), 0, null));
        Assert.Throws<ArgumentException>(() => service.Limit(15_000, Sides.Of(600, 430, 350), null, Sides.Of(100, 70, 10), 600L * 430 * 351, null));
        Assert.Throws<ArgumentException>(() => service.Limit(15_000, Sides.Of(600, 430, 350), null, Sides.Of(700, 70, 10), 1_000, null));
        Assert.Throws<ArgumentException>(() => service.Limit(15_000, Sides.Of(600, 430, 350), null, Sides.Of(100, 70, 10), 1_000, 0));
    }

    [Theory]
    [InlineData(-1, 70, 10)]
    [InlineData(ShippingSettings.MostPackagingWeightGrams + 1, 70, 10)]
    [InlineData(300, 0, 10)]
    [InlineData(300, 101, 10)]
    [InlineData(300, 70, 0)]
    [InlineData(300, 70, ParcelEstimator.MostParcels + 1)]
    public void Shipping_settings_refuse_what_makes_no_sense(int packagingWeightGrams, int fillPercent, int largestParcelOrder)
    {
        var settings = new ShippingSettings(Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Change(packagingWeightGrams, fillPercent, largestParcelOrder));
        Assert.Equal(ShippingSettings.DefaultPacking, settings.Packing);
    }

    [Fact]
    public void A_store_that_never_set_its_shipping_has_the_owner_s_defaults()
    {
        Assert.Equal(new ParcelPacking(WeightGrams: 300, FillPercent: 70, MostParcels: 10), new ShippingSettings(Guid.NewGuid()).Packing);
    }

    private static CarrierService ZBox() =>
        Service("packeta-cz-zbox", "Packeta Z-BOX, Czechia", CarrierServiceKind.Locker, 15_000, Sides.Of(600, 430, 350), null, 600L * 430 * 350, 1);

    private static CarrierService Doorstep() =>
        Service("packeta-cz-home", "Packeta home delivery, Czechia", CarrierServiceKind.Home, 5_000, Sides.Of(700, 700, 700), 1_200, 400L * 400 * 400, null);

    private static CarrierService Service(string key, string name, CarrierServiceKind kind, int weight, Sides largest, int? sum, long volume, int? parcels)
    {
        var service = new CarrierService(key, "packeta", kind, "CZ");
        service.Describe(name, isActive: true, "docs.packeta.com", new DateOnly(2026, 10, 24));
        service.Limit(weight, largest, sum, Sides.Of(100, 70, 10), volume, parcels);

        return service;
    }

    private static ShippingMethod Method(CarrierService service)
    {
        var method = new ShippingMethod(Guid.NewGuid(), "parcel", "Parcel", "packeta", 0m, 21m, Currency.Of("CZK"), requiresPickupPoint: service.DeliversToPoint);
        method.SendBy(ShippingPricing.PerParcel, service.Key, null, null);

        return method;
    }
}

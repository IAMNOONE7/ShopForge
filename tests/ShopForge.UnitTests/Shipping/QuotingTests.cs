using ShopForge.Orders.Domain;
using ShopForge.Orders.Shipping;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Shipping;

namespace ShopForge.UnitTests.Shipping;

// What a cart costs by one method, or why it cannot go (D-219). The rate cards here are the owner's placeholder
// prices for Packeta (D-219), and the services are shaped like the two seeded (D-214, D-218).
public sealed class QuotingTests
{
    private static readonly Currency Koruna = Currency.Of("CZK");
    private static readonly ParcelPacking Packing = new(WeightGrams: 300, FillPercent: 70, MostParcels: 10);

    // At a band's bound it is that band; a gram over, the next; past the heaviest, none.
    [Theory]
    [InlineData(1, 79)]
    [InlineData(2_000, 79)]
    [InlineData(2_001, 99)]
    [InlineData(15_000, 159)]
    [InlineData(15_001, null)]
    public void A_parcel_falls_in_the_band_its_gross_weight_reaches(int grossWeightGrams, int? price)
    {
        var method = ZBoxMethod();

        Assert.Equal(price, (int?)method.BandFor(grossWeightGrams)?.Price);
    }

    // The packaging counts towards the band, because Packeta weighs the parcel (decision 10).
    [Theory]
    [InlineData(1_700, 2_000, 79)]
    [InlineData(1_701, 2_001, 99)]
    public void A_parcel_is_banded_on_its_goods_and_its_packaging(int goodsGrams, int grossGrams, int price)
    {
        var quote = Quoting.PerParcel(ZBoxMethod(), ZBox(), Packing, [Item("DRILL", goodsGrams, 300, 200, 100)], "CZ", Koruna);

        var parcel = Assert.Single(quote.Parcels);
        Assert.Null(quote.Unavailable);
        Assert.Equal((grossGrams, price), (parcel.GrossWeightGrams, (int)parcel.Price));
        Assert.Equal((decimal)price, quote.Total);
        Assert.Equal([new QuotedContent(parcel.Contents[0].VariantId, "DRILL", 1)], parcel.Contents);
    }

    [Fact]
    public void Two_parcels_cost_the_sum_of_their_prices()
    {
        var doorstep = Doorstep();
        var method = Method(doorstep, [new(2_000, 109m), new(5_000, 129m)]);

        var quote = Quoting.PerParcel(method, doorstep, Packing, [Item("ANVIL", 4_500, 300, 150, 120), Item("FILE", 300, 250, 30, 10)], null, Koruna);

        Assert.Equal([129m, 109m], quote.Parcels.Select(parcel => parcel.Price));
        Assert.Equal([5_000, 2_000], quote.Parcels.Select(parcel => parcel.BandUpToGrams));
        Assert.Equal(238m, quote.Total);
    }

    // A parcel above the heaviest band makes the method unavailable; it is never charged the top band's price.
    [Fact]
    public void A_parcel_above_the_heaviest_band_makes_the_method_unavailable()
    {
        var doorstep = Doorstep();
        var method = Method(doorstep, [new(2_000, 109m), new(4_000, 119m)]);

        var quote = Quoting.PerParcel(method, doorstep, Packing, [Item("ANVIL", 4_500, 300, 150, 120)], null, Koruna);

        Assert.Equal(QuoteRefusalReason.AboveHighestBand, quote.Unavailable!.Reason);
        Assert.Equal("A parcel would weigh 4800 g, more than this method's heaviest band, up to 4000 g.", quote.Unavailable.Detail);
        Assert.Empty(quote.Parcels);
        Assert.Equal(0m, quote.Total);
    }

    // Each band is already in the currency's decimals, so the parcels add up exactly in every kind of money (D-186).
    [Theory]
    [InlineData("JPY", "800", "1200", "2000")]
    [InlineData("CZK", "109.50", "129.90", "239.40")]
    [InlineData("KWD", "1.125", "1.875", "3.000")]
    public void Parcels_add_up_exactly_in_any_currency(string code, string light, string heavy, string total)
    {
        var currency = Currency.Of(code);
        var doorstep = Doorstep();
        var method = Method(doorstep, [new(2_000, decimal.Parse(light)), new(5_000, decimal.Parse(heavy))], currency);

        var quote = Quoting.PerParcel(method, doorstep, Packing, [Item("ANVIL", 4_500, 300, 150, 120), Item("FILE", 300, 250, 30, 10)], null, currency);

        Assert.Equal(decimal.Parse(total), quote.Total);
        Assert.Equal(code, quote.Currency);
    }

    [Fact]
    public void A_price_the_currency_cannot_hold_is_refused()
    {
        var method = ZBoxMethod();

        Assert.Throws<ArgumentException>(() => method.PriceParcels(RateSource.Own, null, [new(2_000, 79.5m)], Currency.Of("JPY")));
        Assert.Throws<ArgumentException>(() => method.PriceParcels(RateSource.Own, null, [], Koruna));
        Assert.Throws<ArgumentException>(() => method.PriceParcels(RateSource.Own, null, [new(5_000, 99m), new(5_000, 129m)], Koruna));
        Assert.Throws<ArgumentException>(() => method.PriceParcels(RateSource.Own, null, [new(0, 99m)], Koruna));
        Assert.Throws<ArgumentException>(() => method.PriceParcels(RateSource.Own, null, [new(2_000, -1m)], Koruna));
    }

    // What makes a method unavailable is named: the form to blame, and what it lacks or why it cannot go.
    [Fact]
    public void Missing_data_makes_the_method_unavailable_naming_the_form()
    {
        var weightless = Item("BARE", 0, 300, 200, 100) with { Variant = Variant("BARE", null, 300, 200, 100) };
        var unsized = Item("LOOSE", 500, 300, 200, 100) with { Variant = Variant("LOOSE", 500, null, null, null) };

        var unweighed = Quoting.PerParcel(ZBoxMethod(), ZBox(), Packing, [weightless], "CZ", Koruna);
        var unmeasured = Quoting.PerParcel(ZBoxMethod(), ZBox(), Packing, [unsized], "CZ", Koruna);

        Assert.Equal(new QuoteRefusal(QuoteRefusalReason.MissingWeight, "BARE has no weight, so a parcel holding it cannot be priced.", weightless.Variant.Id), unweighed.Unavailable);
        Assert.Equal(new QuoteRefusal(QuoteRefusalReason.MissingSize, "LOOSE has no length, width and height, so a parcel holding it cannot be priced.", unsized.Variant.Id), unmeasured.Unavailable);
    }

    [Fact]
    public void A_form_the_service_cannot_take_and_too_many_parcels_are_named()
    {
        var table = Item("TABLE", 45_000, 1_800, 900, 120);
        var crate = Item("CRATE", 2_000, 590, 420, 340, quantity: 2);

        var tooLarge = Quoting.PerParcel(ZBoxMethod(), ZBox(), Packing, [table], "CZ", Koruna);
        var tooMany = Quoting.PerParcel(ZBoxMethod(), ZBox(), Packing, [crate], "CZ", Koruna);

        Assert.Equal(new QuoteRefusal(QuoteRefusalReason.ItemTooLarge, "TABLE is larger than Packeta Z-BOX, Czechia takes.", table.Variant.Id), tooLarge.Unavailable);
        Assert.Equal(new QuoteRefusal(QuoteRefusalReason.TooManyParcels, "The order would need more than 1 parcel, more than is sent by parcel.", null), tooMany.Unavailable);
    }

    [Fact]
    public void A_method_without_its_service_prices_or_destination_cannot_quote()
    {
        var zbox = ZBox();
        var unused = ZBox();
        unused.Describe(unused.Name, isActive: false, unused.Source, unused.SourceDate);
        var unpriced = ZBoxMethod();
        unpriced.ForgetRates();
        var item = Item("DRILL", 1_000, 300, 200, 100);

        Assert.Equal(QuoteRefusalReason.ServiceUnavailable, Quoting.PerParcel(ZBoxMethod(), null, Packing, [item], "CZ", Koruna).Unavailable!.Reason);
        Assert.Equal(QuoteRefusalReason.ServiceUnavailable, Quoting.PerParcel(ZBoxMethod(), unused, Packing, [item], "CZ", Koruna).Unavailable!.Reason);
        Assert.Equal(QuoteRefusalReason.ServiceUnavailable, Quoting.PerParcel(OwnDelivery(zbox), zbox, Packing, [item], "CZ", Koruna).Unavailable!.Reason);
        Assert.Equal(QuoteRefusalReason.NoRates, Quoting.PerParcel(unpriced, zbox, Packing, [item], "CZ", Koruna).Unavailable!.Reason);
        Assert.Equal("Packeta Z-BOX, Czechia delivers only to CZ.", Quoting.PerParcel(ZBoxMethod(), zbox, Packing, [item], "SK", Koruna).Unavailable!.Detail);
    }

    // A flat method answers its one price, and D-145's limits, through the same rules.
    [Fact]
    public void A_flat_method_s_quote_is_its_price_within_its_limits()
    {
        var courier = new ShippingMethod(Guid.NewGuid(), "courier", "Courier", "manual", 129m, 21m, Koruna, maxWeightGrams: 30_000, countries: ["CZ"]);
        var item = Item("DRILL", 2_600, 420, 330, 110);

        var quote = Quoting.Flat(courier, [item], "CZ", Koruna);
        var elsewhere = Quoting.Flat(courier, [item], "SK", Koruna);
        var tooHeavy = Quoting.Flat(courier, [item with { Quantity = 12 }], "CZ", Koruna);
        var unweighed = Quoting.Flat(courier, [item with { Variant = Variant("BARE", null, 300, 200, 100) }], "CZ", Koruna);

        Assert.Equal((129m, 0), (quote.Total, quote.Parcels.Count));
        Assert.Equal(QuoteRefusalReason.NotServed, elsewhere.Unavailable!.Reason);
        Assert.Equal("The order weighs 31200 g; this method takes up to 30000 g.", tooHeavy.Unavailable!.Detail);
        Assert.Equal(QuoteRefusalReason.MissingWeight, unweighed.Unavailable!.Reason);
    }

    [Fact]
    public void A_flat_method_without_a_weight_limit_takes_what_nobody_weighed()
    {
        var pickup = new ShippingMethod(Guid.NewGuid(), "pickup", "Pickup", "manual", 0m, 21m, Koruna);

        Assert.Null(Quoting.Flat(pickup, [Item("BARE", 0, 1, 1, 1) with { Variant = Variant("BARE", null, null, null, null) }], null, Koruna).Unavailable);
    }

    // What a box is asked about (D-220): the heaviest parcel, and each sorted side at its longest across the cart, so a box
    // that cannot take the order's largest things is neither shown nor accepted. A flat method asks as before.
    [Fact]
    public void A_box_is_asked_about_the_heaviest_parcel_and_the_carts_largest_sides()
    {
        var doorstep = Doorstep();
        var method = Method(doorstep, [new(2_000, 109m), new(5_000, 129m)]);
        QuotedItem[] items = [Item("ANVIL", 4_500, 300, 150, 120), Item("FILE", 300, 251, 30, 10), Item("RULER", 50, 30, 200, 160)];

        var quote = Quoting.PerParcel(method, doorstep, Packing, items, "CZ", Koruna);
        var courier = new ShippingMethod(Guid.NewGuid(), "courier", "Courier", "manual", 129m, 21m, Koruna);

        // The ruler shares the anvil's parcel (4550 g and 300 g of packaging), and its middle side, 160 mm, is longer than the
        // anvil's.
        Assert.Equal(new Parcel(4_850, "CZ", 300, 160, 120), ShippingQuotes.ForPoint(quote, items, "CZ"));
        Assert.Equal(new Parcel(null, "CZ"), ShippingQuotes.ForPoint(Quoting.Flat(courier, items, "CZ", Koruna), items, "CZ"));
    }

    // A carrier that takes whole centimetres is given them rounded up.
    [Theory]
    [InlineData(450, 45)]
    [InlineData(451, 46)]
    [InlineData(1, 1)]
    public void A_side_in_centimetres_is_rounded_up(int millimetres, int centimetres)
    {
        Assert.Equal(centimetres, Parcel.Centimetres(millimetres));
        Assert.Null(Parcel.Centimetres(null));
    }

    // A cart with nothing in it is as unweighed as one with a form nobody weighed, as a checkout without a cart always
    // was (D-145).
    [Fact]
    public void A_weight_limit_cannot_be_promised_about_an_empty_cart()
    {
        var courier = new ShippingMethod(Guid.NewGuid(), "courier", "Courier", "manual", 129m, 21m, Koruna, maxWeightGrams: 30_000);

        var quote = Quoting.Flat(courier, [], null, Koruna);

        Assert.Null(Quoting.WeightOf([]));
        Assert.Equal(new QuoteRefusal(QuoteRefusalReason.MissingWeight, "Nothing in the order is weighed, and this method takes up to 30000 g.", null), quote.Unavailable);
        Assert.Equal(2_600 + 600, Quoting.WeightOf([Item("DRILL", 2_600, 420, 330, 110), Item("FILE", 300, 250, 30, 10, quantity: 2)]));
    }

    // The owner's placeholder prices, in korunas only, and never over the merchant's own.
    [Fact]
    public void A_method_starts_from_its_service_s_placeholder_prices_and_keeps_its_own()
    {
        var method = Method(ZBox(), bands: null);

        PlaceholderRates.StartFrom(method, Koruna);
        var started = (method.RatesFrom, method.RateBands.Select(band => (band.UpToGrams, band.Price)).ToList());
        method.PriceParcels(RateSource.Own, null, [new(2_000, 89m)], Koruna);
        PlaceholderRates.StartFrom(method, Koruna);

        Assert.Equal(RateSource.Placeholder, started.RatesFrom);
        Assert.Equal([(2_000, 79m), (5_000, 99m), (10_000, 129m), (15_000, 159m)], started.Item2);
        Assert.Equal(RateSource.Own, method.RatesFrom);
        Assert.Equal([new RateBand(2_000, 89m)], method.RateBands);
        Assert.Equal([new RateBand(2_000, 109m), new RateBand(5_000, 129m)], PlaceholderRates.For("packeta-cz-home", Koruna));
        Assert.Null(PlaceholderRates.For("packeta-cz-zbox", Currency.Of("EUR")));
        Assert.Null(PlaceholderRates.For("somebody-else", Koruna));
    }

    [Fact]
    public void A_method_with_no_placeholder_for_its_service_starts_with_no_prices()
    {
        var method = ZBoxMethod();

        PlaceholderRates.StartFrom(method, Currency.Of("EUR"));

        Assert.Empty(method.RateBands);
        Assert.Null(method.RatesFrom);
    }

    // A flat method has one price and no rate card.
    [Fact]
    public void A_method_turned_flat_forgets_its_rate_card()
    {
        var method = ZBoxMethod();

        method.SendBy(ShippingPricing.Flat, null, null, null);

        Assert.Empty(method.RateBands);
        Assert.Null(method.RatesFrom);
        Assert.Throws<ArgumentException>(() => method.PriceParcels(RateSource.Own, null, [new(2_000, 79m)], Koruna));
    }

    private static QuotedItem Item(string sku, int weight, int length, int width, int height, int quantity = 1) =>
        new(Variant(sku, weight, length, width, height), quantity);

    private static SellableVariant Variant(string sku, int? weight, int? length, int? width, int? height) =>
        new(Guid.NewGuid(), sku, [], 0, weight, length, width, height, ShipsAlone: false);

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

    // A method of the store's own delivery pointed at a service another provider carries.
    private static ShippingMethod OwnDelivery(CarrierService service)
    {
        var method = new ShippingMethod(Guid.NewGuid(), "own", "Own", "manual", 0m, 21m, Koruna, requiresPickupPoint: service.DeliversToPoint);
        method.SendBy(ShippingPricing.PerParcel, service.Key, null, null);
        method.PriceParcels(RateSource.Own, null, [new(15_000, 99m)], Koruna);

        return method;
    }

    private static ShippingMethod ZBoxMethod() =>
        Method(ZBox(), [new(2_000, 79m), new(5_000, 99m), new(10_000, 129m), new(15_000, 159m)]);

    private static ShippingMethod Method(CarrierService service, IReadOnlyList<RateBand>? bands, Currency? currency = null)
    {
        var method = new ShippingMethod(Guid.NewGuid(), "parcel", "Parcel", "packeta", 0m, 21m, currency ?? Koruna, requiresPickupPoint: service.DeliversToPoint);
        method.SendBy(ShippingPricing.PerParcel, service.Key, null, null);

        if (bands is not null)
        {
            method.PriceParcels(RateSource.Placeholder, null, bands, currency ?? Koruna);
        }

        return method;
    }
}

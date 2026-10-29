using ShopForge.Orders.Domain;
using ShopForge.Orders.Shipping;
using ShopForge.Shared.Payments;

namespace ShopForge.UnitTests.Shipping;

// Delivery costs nothing once the goods come to the store's threshold (D-221).
public sealed class FreeShippingTests
{
    private static readonly Currency Koruna = Currency.Of("CZK");

    [Theory]
    [InlineData(1_000, true)]
    [InlineData(999.99, false)]
    [InlineData(1_500, true)]
    public void Exactly_the_threshold_reaches_it_and_a_haler_below_does_not(decimal goods, bool free)
    {
        Assert.Equal(free, FreeShipping.Applies(1_000m, goods));
    }

    [Fact]
    public void Switched_off_it_never_applies_and_says_nothing_about_what_remains()
    {
        Assert.False(FreeShipping.Applies(null, 1_000_000m));
        Assert.Null(FreeShipping.Remaining(null, 10m));
    }

    [Theory]
    [InlineData(640, 360)]
    [InlineData(999.99, 0.01)]
    [InlineData(1_000, 0)]
    [InlineData(1_500, 0)]
    public void What_remains_is_the_difference_and_nothing_once_reached(decimal goods, decimal remaining)
    {
        Assert.Equal(remaining, FreeShipping.Remaining(1_000m, goods));
    }

    // The quote keeps the price, so the order can keep it beside the discount, as it keeps a code's.
    [Fact]
    public void Free_delivery_keeps_the_price_and_takes_all_of_it_off()
    {
        var quote = FreeShipping.Apply(Priced(89m), 1_000m, 1_000m);

        Assert.Equal((89m, 89m, 0m, (decimal?)1_000m), (quote.Total, quote.ShippingDiscount, quote.Charged, quote.FreeShippingThreshold));
    }

    [Theory]
    [InlineData(999.99, 1_000)]
    [InlineData(5_000, null)]
    public void Below_the_threshold_or_switched_off_the_quote_is_untouched(decimal goods, int? threshold)
    {
        var quote = FreeShipping.Apply(Priced(89m), threshold, goods);

        Assert.Equal((89m, 0m, 89m, (decimal?)null), (quote.Total, quote.ShippingDiscount, quote.Charged, quote.FreeShippingThreshold));
    }

    // Nothing to count is not a cart that reached anything: the cheapest delivery a store states is not free.
    [Fact]
    public void Without_goods_to_count_delivery_costs_what_it_costs()
    {
        Assert.Equal(89m, FreeShipping.Apply(Priced(89m), 1_000m, goods: null).Charged);
    }

    [Fact]
    public void A_method_that_cannot_go_or_costs_nothing_is_left_as_it_is()
    {
        var refused = ShippingQuote.Refused(Koruna, QuoteRefusalReason.TooHeavy, "Too heavy.");
        var nothing = Priced(0m);

        Assert.Same(refused, FreeShipping.Apply(refused, 1_000m, 5_000m));
        Assert.Same(nothing, FreeShipping.Apply(nothing, 1_000m, 5_000m));
    }

    private static ShippingQuote Priced(decimal total) => new(Koruna.Code, total, [], null);
}

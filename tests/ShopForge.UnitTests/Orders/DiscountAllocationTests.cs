using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;

namespace ShopForge.UnitTests.Orders;

public sealed class DiscountAllocationTests
{
    private static readonly Currency Euro = Currency.Of("EUR");

    private static readonly Currency Yen = Currency.Of("JPY");

    [Fact]
    public void A_percentage_is_split_across_the_lines_it_came_off()
    {
        var discount = Percentage(10m);

        var result = DiscountAllocation.For(discount, [100m, 300m], shippingPrice: 4.90m, Euro);

        Assert.Equal([10m, 30m], result.LineDiscounts);
        Assert.Equal(0m, result.ShippingDiscount);
        Assert.Equal(40m, result.Total);
    }

    // 10 % of 10.00 is 1.00, but three equal shares of it are 0.33 each. The odd hundredth has to land
    // somewhere, or the parts stop adding up to the discount.
    [Fact]
    public void The_hundredths_that_do_not_divide_go_to_the_largest_line()
    {
        var discount = Percentage(10m);

        var result = DiscountAllocation.For(discount, [3.33m, 3.33m, 3.34m], shippingPrice: 0m, Euro);

        Assert.Equal([0.33m, 0.33m, 0.34m], result.LineDiscounts);
        Assert.Equal(1.00m, result.Total);
    }

    [Fact]
    public void An_amount_larger_than_the_cart_takes_the_cart_to_nothing_and_no_further()
    {
        var discount = Amount(50m);

        var result = DiscountAllocation.For(discount, [12m, 8m], shippingPrice: 4.90m, Euro);

        Assert.Equal(20m, result.Total);
        Assert.Equal([12m, 8m], result.LineDiscounts);
    }

    [Fact]
    public void An_amount_is_split_in_proportion_to_what_each_line_costs()
    {
        var discount = Amount(7m);

        var result = DiscountAllocation.For(discount, [10m, 90m], shippingPrice: 0m, Euro);

        Assert.Equal([0.70m, 6.30m], result.LineDiscounts);
        Assert.Equal(7m, result.Total);
    }

    [Fact]
    public void Free_shipping_touches_the_shipping_line_only()
    {
        var discount = new Discount(Guid.NewGuid(), "SHIPFREE", "Free shipping", DiscountKind.FreeShipping, 0m, new DiscountLimits(), Euro);

        var result = DiscountAllocation.For(discount, [100m, 50m], shippingPrice: 4.90m, Euro);

        Assert.Equal([0m, 0m], result.LineDiscounts);
        Assert.Equal(4.90m, result.ShippingDiscount);
        Assert.Equal(4.90m, result.Total);
    }

    [Theory]
    [InlineData(33.33)]
    [InlineData(7.77)]
    [InlineData(99.99)]
    public void Whatever_the_percentage_the_parts_add_up_to_the_whole(decimal percentage)
    {
        var discount = Percentage(percentage);
        decimal[] lines = [19.99m, 5.45m, 120m, 0.99m, 7.50m];

        var result = DiscountAllocation.For(discount, lines, shippingPrice: 0m, Euro);
        var expected = decimal.Round(lines.Sum() * percentage / 100m, 2, MidpointRounding.AwayFromZero);

        Assert.Equal(expected, result.LineDiscounts.Sum());
        Assert.All(result.LineDiscounts.Select((share, index) => (share, line: lines[index])), pair => Assert.True(pair.share <= pair.line));
    }

    // The invariant that matters: whatever the cart looks like, the parts add up to the discount and no line is
    // discounted below nothing.
    [Fact]
    public void The_parts_always_add_up_and_no_line_goes_below_nothing()
    {
        var random = new Random(20260924);

        for (var attempt = 0; attempt < 500; attempt++)
        {
            var lines = Enumerable.Range(0, random.Next(1, 8))
                .Select(_ => decimal.Round((decimal)(random.NextDouble() * 500) + 0.01m, 2))
                .ToList();
            var percentage = decimal.Round((decimal)(random.NextDouble() * 100), 2);
            percentage = percentage <= 0 ? 1m : percentage;

            var result = DiscountAllocation.For(Percentage(percentage), lines, shippingPrice: 0m, Euro);
            var expected = decimal.Round(lines.Sum() * percentage / 100m, 2, MidpointRounding.AwayFromZero);

            Assert.Equal(expected, result.LineDiscounts.Sum());
            Assert.All(result.LineDiscounts.Select((share, index) => (share, line: lines[index])), pair =>
            {
                Assert.True(pair.share >= 0);
                Assert.True(pair.share <= pair.line);
            });
        }
    }

    // In a currency with no minor unit there are no hundredths to hand around: the shares are whole yen, and
    // they still add up to the discount rather than to something a yen off it.
    [Fact]
    public void A_currency_with_no_minor_unit_is_split_in_whole_units()
    {
        var result = DiscountAllocation.For(Percentage(10m), [1000m, 1000m, 1001m], shippingPrice: 0m, Yen);

        Assert.All(result.LineDiscounts, share => Assert.Equal(decimal.Truncate(share), share));
        Assert.Equal(300m, result.Total);
    }

    private static Discount Percentage(decimal value) =>
        new(Guid.NewGuid(), "TENOFF", "Ten off", DiscountKind.Percentage, value, new DiscountLimits(), Euro);

    private static Discount Amount(decimal value) =>
        new(Guid.NewGuid(), "FIVER", "Five off", DiscountKind.Amount, value, new DiscountLimits(), Euro);
}

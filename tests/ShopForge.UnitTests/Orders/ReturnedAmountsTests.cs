using ShopForge.Orders.Returns;

namespace ShopForge.UnitTests.Orders;

public sealed class ReturnedAmountsTests
{
    [Fact]
    public void Half_a_line_takes_half_its_discount()
    {
        var share = ReturnedAmounts.DiscountShare(discount: 10m, quantity: 4, alreadyReturned: 0, returning: 2);

        Assert.Equal(5m, share);
    }

    // A third of 1.00 is 0.33; the hundredth that does not divide comes back with one of the three, and the three
    // still add up to what was given.
    [Fact]
    public void One_item_of_a_line_takes_the_hundredth_that_did_not_divide()
    {
        var first = ReturnedAmounts.DiscountShare(1m, quantity: 3, alreadyReturned: 0, returning: 1);
        var second = ReturnedAmounts.DiscountShare(1m, quantity: 3, alreadyReturned: 1, returning: 1);
        var third = ReturnedAmounts.DiscountShare(1m, quantity: 3, alreadyReturned: 2, returning: 1);

        Assert.Equal([0.33m, 0.34m, 0.33m], new[] { first, second, third });
        Assert.Equal(1m, first + second + third);
    }

    [Fact]
    public void A_line_that_was_not_discounted_gives_nothing_back()
    {
        Assert.Equal(0m, ReturnedAmounts.DiscountShare(0m, quantity: 3, alreadyReturned: 1, returning: 2));
    }

    // However a line is split, and in whatever order its parcels arrive, the shop gives back exactly the discount
    // it gave — never a hundredth more or less.
    [Fact]
    public void The_shares_of_a_line_always_add_up_to_its_discount()
    {
        var random = new Random(14);

        for (var attempt = 0; attempt < 500; attempt++)
        {
            var quantity = random.Next(1, 12);
            var discount = decimal.Round((decimal)(random.NextDouble() * 100), 2);
            var returned = 0;
            var shares = 0m;

            while (returned < quantity)
            {
                var returning = random.Next(1, quantity - returned + 1);

                shares += ReturnedAmounts.DiscountShare(discount, quantity, returned, returning);
                returned += returning;
            }

            Assert.Equal(discount, shares);
        }
    }
}

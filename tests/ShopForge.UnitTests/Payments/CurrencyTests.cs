using ShopForge.Shared.Payments;

namespace ShopForge.UnitTests.Payments;

public sealed class CurrencyTests
{
    [Theory]
    [InlineData("EUR", 2)]
    [InlineData("CZK", 2)]
    [InlineData("HUF", 2)]
    [InlineData("JPY", 0)]
    [InlineData("ISK", 0)]
    [InlineData("KRW", 0)]
    [InlineData("KWD", 3)]
    [InlineData("TND", 3)]
    public void A_currency_knows_how_many_decimals_it_has(string code, int decimals)
    {
        Assert.Equal(decimals, Currency.Of(code).Decimals);
    }

    // The table the slice exists for: the same number rounds differently depending on what it is money in.
    [Theory]
    [InlineData("EUR", 1.005, 1.01)]
    [InlineData("EUR", 1.004, 1.00)]
    [InlineData("EUR", -1.005, -1.01)]
    [InlineData("EUR", 2.675, 2.68)]
    [InlineData("JPY", 1.5, 2)]
    [InlineData("JPY", 1.4, 1)]
    [InlineData("JPY", -1.5, -2)]
    [InlineData("JPY", 1000, 1000)]
    [InlineData("KWD", 1.0005, 1.001)]
    [InlineData("KWD", 1.0004, 1.000)]
    [InlineData("KWD", -1.0005, -1.001)]
    public void Rounding_follows_the_currency(string code, decimal amount, decimal expected)
    {
        Assert.Equal(expected, Currency.Of(code).Round(amount));
    }

    [Theory]
    [InlineData("EUR", 1.009, 1.00)]
    [InlineData("EUR", -1.009, -1.00)]
    [InlineData("JPY", 1.9, 1)]
    [InlineData("KWD", 1.0009, 1.000)]
    public void Rounding_down_never_moves_away_from_nothing(string code, decimal amount, decimal expected)
    {
        Assert.Equal(expected, Currency.Of(code).RoundDown(amount));
    }

    // What the gateway is told. Getting this wrong by a factor of a hundred is the whole reason the type exists.
    [Theory]
    [InlineData("EUR", 204.90, 20490)]
    [InlineData("EUR", 0.01, 1)]
    [InlineData("EUR", 1.005, 101)]
    [InlineData("CZK", 1290, 129000)]
    [InlineData("JPY", 1500, 1500)]
    [InlineData("JPY", 1, 1)]
    [InlineData("ISK", 4990, 4990)]
    [InlineData("KWD", 1.234, 1234)]
    [InlineData("KWD", 10, 10000)]
    public void Minor_units_are_the_currency_own_smallest_unit(string code, decimal amount, long expected)
    {
        Assert.Equal(expected, Currency.Of(code).ToMinorUnits(amount));
    }

    [Theory]
    [InlineData("EUR", 10.50, true)]
    [InlineData("EUR", 10.505, false)]
    [InlineData("JPY", 10, true)]
    [InlineData("JPY", 10.5, false)]
    [InlineData("KWD", 1.234, true)]
    [InlineData("KWD", 1.2345, false)]
    public void A_currency_says_which_amounts_it_can_express(string code, decimal amount, bool held)
    {
        Assert.Equal(held, Currency.Of(code).Holds(amount));
    }

    [Theory]
    [InlineData("eur")]
    [InlineData(" EUR ")]
    [InlineData("Eur")]
    public void A_code_is_read_however_it_is_written(string code)
    {
        Assert.Equal(Currency.Of("EUR"), Currency.Of(code));
    }

    [Theory]
    [InlineData("ZWL")]
    [InlineData("XXX")]
    [InlineData("CZ")]
    [InlineData("CZK1")]
    [InlineData("")]
    [InlineData(null)]
    public void A_currency_ShopForge_cannot_charge_in_is_not_a_currency(string? code)
    {
        Assert.Null(Currency.Find(code));
        Assert.Throws<ArgumentException>(() => Currency.Of(code));
    }

    [Fact]
    public void The_same_code_is_the_same_currency()
    {
        Assert.Equal(Currency.Of("EUR"), Currency.Of("EUR"));
        Assert.NotEqual(Currency.Of("EUR"), Currency.Of("CZK"));
        Assert.Equal("EUR", Currency.Of("EUR").ToString());
    }
}

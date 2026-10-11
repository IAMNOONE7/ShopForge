using ShopForge.Shared.Tax;

namespace ShopForge.UnitTests.Tax;

public sealed class DestinationRatesTests
{
    private static readonly DestinationRates German = new("DE", new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        ["standard"] = 19m,
        ["books"] = 7m,
    });

    [Fact]
    public void A_class_is_charged_what_the_destination_charges_for_it()
    {
        Assert.Equal(7m, German.For("books", fallback: 21m));
        Assert.Equal(19m, German.For("standard", fallback: 21m));
    }

    // A thing that names no class is taxed at the rate it carries, which is how every shop that sells in one
    // country goes on working.
    [Fact]
    public void A_thing_that_names_no_class_keeps_its_own_rate()
    {
        Assert.Equal(21m, German.For(null, fallback: 21m));
        Assert.Equal(21m, German.For(string.Empty, fallback: 21m));
    }

    // A class the table says nothing about is not an excuse to charge nothing.
    [Fact]
    public void A_class_nobody_has_priced_falls_back_to_the_rate_the_thing_carries()
    {
        Assert.Equal(21m, German.For("food", fallback: 21m));
    }

    [Fact]
    public void A_shop_with_no_rules_charges_what_its_things_say()
    {
        Assert.Equal(21m, DestinationRates.Nowhere.For("books", fallback: 21m));
        Assert.Null(DestinationRates.Nowhere.Country);
    }

    // Zero is a rate and not a missing one: an export charged at nothing must not fall back to the shop's own.
    [Fact]
    public void A_rate_of_nothing_is_a_rate()
    {
        var exempt = new DestinationRates("US", new Dictionary<string, decimal>(StringComparer.Ordinal) { ["books"] = 0m });

        Assert.Equal(0m, exempt.For("books", fallback: 21m));
    }
}

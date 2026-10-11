using ShopForge.Stores.Domain;

namespace ShopForge.UnitTests.Stores;

// The gate store creation goes through. Before it, any host name a tenant typed became a proved domain, and host
// names are unique platform-wide, so a company could take a name it did not own and nobody else could ever add
// it (D-189).
public sealed class PlatformHostsTests
{
    private static readonly PlatformHosts Hosts = new() { Suffix = "shops.example.cz" };

    [Theory]
    [InlineData("wooden-home", "wooden-home")]
    [InlineData("wooden-home.shops.example.cz", "wooden-home")]
    [InlineData("  Wooden-Home.Shops.Example.CZ  ", "wooden-home")]
    [InlineData("wooden-home.shops.example.cz.", "wooden-home")]
    [InlineData("a", "a")]
    // Host names are case-insensitive, so a merchant typing capitals gets a lower-case name rather than a
    // refusal. The whole point of reading a label is to be generous about how it was typed.
    [InlineData("WOODEN-HOME", "wooden-home")]
    public void A_label_is_read_bare_or_under_the_suffix(string given, string expected)
    {
        Assert.Equal(expected, Hosts.LabelOf(given));
    }

    [Theory]
    [InlineData("wooden-home.cz")]
    [InlineData("www.somebody-elses-shop.cz")]
    [InlineData("shops.example.cz")]
    [InlineData("deep.wooden-home.shops.example.cz")]
    [InlineData("under_score")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_the_platform_does_not_own_is_not_a_label(string? given)
    {
        Assert.Null(Hosts.LabelOf(given));
    }

    [Fact]
    public void A_label_longer_than_a_dns_label_is_refused()
    {
        Assert.NotNull(Hosts.LabelOf(new string('a', PlatformHosts.MaxLabelLength)));
        Assert.Null(Hosts.LabelOf(new string('a', PlatformHosts.MaxLabelLength + 1)));
    }

    [Fact]
    public void Composing_puts_the_label_under_the_suffix()
    {
        Assert.Equal("wooden-home.shops.example.cz", Hosts.Compose("wooden-home"));
        Assert.Throws<ArgumentException>(() => Hosts.Compose("wooden-home.cz"));
    }

    [Theory]
    [InlineData("wooden-home.shops.example.cz", true)]
    [InlineData("deep.wooden-home.shops.example.cz", true)]
    [InlineData("shops.example.cz", false)]
    [InlineData("notshops.example.cz", false)]
    [InlineData("wooden-home.cz", false)]
    public void The_platform_owns_only_what_is_under_its_suffix(string hostName, bool owned)
    {
        Assert.Equal(owned, Hosts.Owns(hostName));
    }

    // A platform that has not been told what it owns hands out nothing. Refusing is the only safe answer: the
    // alternative is trusting whatever arrives, which is the defect this class exists to close.
    [Fact]
    public void A_platform_with_no_suffix_gives_out_no_names()
    {
        var unconfigured = new PlatformHosts();

        Assert.False(unconfigured.IsConfigured);
        Assert.Null(unconfigured.LabelOf("wooden-home"));
        Assert.False(unconfigured.Owns("wooden-home.shops.example.cz"));
        Assert.Throws<ArgumentException>(() => unconfigured.Compose("wooden-home"));
    }

    // On this machine the suffix is a single label, which the composition has to handle as readily as a zone.
    [Fact]
    public void A_one_label_suffix_works_the_same_way()
    {
        var local = new PlatformHosts { Suffix = "localhost" };

        Assert.Equal("shop-a.localhost", local.Compose("shop-a"));
        Assert.Equal("shop-a", local.LabelOf("shop-a.localhost"));
        Assert.True(local.Owns("shop-a.localhost"));
        Assert.False(local.Owns("localhost"));
    }
}

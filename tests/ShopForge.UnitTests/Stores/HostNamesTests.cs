using ShopForge.Stores.Domain;

namespace ShopForge.UnitTests.Stores;

public sealed class HostNamesTests
{
    [Theory]
    [InlineData("shop.example.com", "shop.example.com")]
    [InlineData("Shop.Example.COM", "shop.example.com")]
    [InlineData("shop.example.com:8080", "shop.example.com")]
    [InlineData("shop.example.com.", "shop.example.com")]
    [InlineData("  shop.example.com  ", "shop.example.com")]
    [InlineData("SHOP-A.localhost:5173", "shop-a.localhost")]
    [InlineData("Dřevěný-Domov.cz", "xn--devn-domov-oeb27ax0a.cz")]
    public void Normalize_returns_canonical_host_name(string host, string expected)
    {
        Assert.Equal(expected, HostNames.Normalize(host));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":8080")]
    [InlineData(".")]
    [InlineData("not a host")]
    [InlineData("under_score.test")]
    [InlineData("shop..example.com")]
    [InlineData("-shop.example.com")]
    public void Normalize_returns_null_for_invalid_host(string? host)
    {
        Assert.Null(HostNames.Normalize(host));
    }
}

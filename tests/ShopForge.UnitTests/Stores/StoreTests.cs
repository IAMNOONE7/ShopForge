using ShopForge.Stores.Domain;

namespace ShopForge.UnitTests.Stores;

public sealed class StoreTests
{
    private static readonly StoreTheme Theme = new("#8B5A2B", "#F5F0E8", 8);

    [Fact]
    public void First_domain_becomes_primary()
    {
        var store = new Store(Guid.NewGuid(), "Wooden Home", "czk", "cs-CZ", Theme);

        var first = store.AddDomain("wooden-home.cz");
        var second = store.AddDomain("www.wooden-home.cz");

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);
        Assert.Equal("CZK", store.Currency);
    }

    [Fact]
    public void Same_domain_cannot_be_added_twice()
    {
        var store = new Store(Guid.NewGuid(), "Wooden Home", "CZK", "cs-CZ", Theme);
        store.AddDomain("wooden-home.cz");

        Assert.Throws<InvalidOperationException>(() => store.AddDomain("Wooden-Home.cz:443"));
    }

    [Theory]
    [InlineData("CZ")]
    [InlineData("CZK1")]
    [InlineData("C2K")]
    public void Invalid_currency_is_rejected(string currency)
    {
        Assert.Throws<ArgumentException>(() => new Store(Guid.NewGuid(), "Wooden Home", currency, "cs-CZ", Theme));
    }

    [Fact]
    public void Unknown_culture_is_rejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Store(Guid.NewGuid(), "Wooden Home", "CZK", "xx-NOPE", Theme));
    }

    [Theory]
    [InlineData("8B5A2B")]
    [InlineData("#8B5A2")]
    [InlineData("brown")]
    public void Theme_rejects_colors_that_are_not_hex(string color)
    {
        Assert.Throws<ArgumentException>(() => new StoreTheme(color, "#F5F0E8", 8));
    }
}

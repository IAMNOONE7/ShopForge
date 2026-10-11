using System.Globalization;
using ShopForge.Stores.Domain;

namespace ShopForge.UnitTests.Stores;

public sealed class StoreTests
{
    private static readonly StoreTheme Theme = new("#8B5A2B", "#F5F0E8", 8);

    private static readonly PlatformHosts Hosts = new() { Suffix = "shops.example.cz" };

    [Fact]
    public void First_domain_becomes_primary()
    {
        var store = new Store(Guid.NewGuid(), "Wooden Home", "czk", "cs-CZ", Theme);

        var first = store.AddPlatformDomain(Hosts, "wooden-home", DateTimeOffset.UtcNow);
        var second = store.ClaimDomain("www.wooden-home.cz", DateTimeOffset.UtcNow);

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);
        Assert.Equal("wooden-home.shops.example.cz", first.HostName);
        Assert.Equal("CZK", store.Currency.Code);
    }

    // The name the platform gives out is proved on sight because the platform owns the zone; a name the merchant
    // types is not, and it carries the challenge (D-124, D-189).
    [Fact]
    public void A_platform_name_is_proved_and_a_claimed_one_is_not()
    {
        var store = new Store(Guid.NewGuid(), "Wooden Home", "CZK", "cs-CZ", Theme);

        var given = store.AddPlatformDomain(Hosts, "wooden-home", DateTimeOffset.UtcNow);
        var claimed = store.ClaimDomain("wooden-home.cz", DateTimeOffset.UtcNow);

        Assert.True(given.IsVerified);
        Assert.Null(given.VerificationToken);
        Assert.Null(given.ClaimExpiresAt);
        Assert.False(claimed.IsVerified);
        Assert.NotNull(claimed.VerificationToken);
        Assert.Equal("_shopforge-challenge.wooden-home.cz", claimed.ChallengeName);
    }

    // The defect this guards: before labels, store creation took any host name and marked it proved, and host
    // names are unique platform-wide — so one company could deny another its own domain (D-189).
    [Theory]
    [InlineData("wooden-home.cz")]
    [InlineData("www.somebody-elses-shop.cz")]
    [InlineData("shops.example.cz")]
    [InlineData("a.b")]
    [InlineData("Wooden-Home")]
    [InlineData("under_score")]
    [InlineData("")]
    public void A_name_the_platform_does_not_own_cannot_be_given_to_a_store(string label)
    {
        var store = new Store(Guid.NewGuid(), "Wooden Home", "CZK", "cs-CZ", Theme);

        Assert.Throws<ArgumentException>(() => store.AddPlatformDomain(Hosts, label, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Same_domain_cannot_be_added_twice()
    {
        var store = new Store(Guid.NewGuid(), "Wooden Home", "CZK", "cs-CZ", Theme);
        store.ClaimDomain("wooden-home.cz", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => store.ClaimDomain("Wooden-Home.cz:443", DateTimeOffset.UtcNow));
    }

    // A claim does not hold a name for ever: the name is unique platform-wide, so one nobody proves is one its
    // real owner cannot add (D-189).
    [Fact]
    public void An_unproved_claim_lapses_and_a_proved_one_does_not()
    {
        var claimed = DateTimeOffset.Parse("2026-10-11T09:00:00Z", CultureInfo.InvariantCulture);
        var store = new Store(Guid.NewGuid(), "Wooden Home", "CZK", "cs-CZ", Theme);
        var given = store.AddPlatformDomain(Hosts, "wooden-home", claimed);
        var claim = store.ClaimDomain("wooden-home.cz", claimed);

        Assert.Equal(claimed + StoreDomain.ClaimHoldsFor, claim.ClaimExpiresAt);
        Assert.False(claim.ClaimHasLapsed(claim.ClaimExpiresAt!.Value.AddTicks(-1)));
        Assert.True(claim.ClaimHasLapsed(claim.ClaimExpiresAt!.Value));
        Assert.False(given.ClaimHasLapsed(claimed.AddYears(1)));

        claim.Verify(claimed.AddDays(1));

        Assert.Null(claim.ClaimExpiresAt);
        Assert.False(claim.ClaimHasLapsed(claimed.AddYears(1)));
    }

    [Theory]
    [InlineData("CZ")]
    [InlineData("CZK1")]
    [InlineData("C2K")]
    // A well-formed code ShopForge has no minor units for: refused while the shop is a draft rather than at
    // somebody's checkout.
    [InlineData("ZWL")]
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

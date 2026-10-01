using Microsoft.Extensions.Configuration;
using ShopForge.Infrastructure.Secrets;
using ShopForge.Shared.Security;

namespace ShopForge.UnitTests.Security;

// A secret's name is written into a row by one part of the system and handed to a vault by another, so what
// counts as a name is worth pinning before anything stores one (D-139).
public sealed class SecretNamesTests
{
    [Theory]
    [InlineData("comgate-secret")]
    [InlineData("a")]
    [InlineData("store-01a0-comgate-secret")]
    public void A_name_of_letters_digits_and_hyphens_is_usable(string name) => Assert.True(SecretNames.IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Comgate-Secret")]
    [InlineData("comgate secret")]
    [InlineData("comgate/secret")]
    [InlineData("../other-store-secret")]
    [InlineData("comgate_secret")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    public void Anything_that_could_mean_a_different_secret_is_not(string? name) => Assert.False(SecretNames.IsValid(name));

    [Fact]
    public void A_name_longer_than_a_vault_accepts_is_not_usable()
    {
        Assert.True(SecretNames.IsValid(new string('a', SecretNames.MaxLength)));
        Assert.False(SecretNames.IsValid(new string('a', SecretNames.MaxLength + 1)));
    }

    [Fact]
    public async Task Asking_for_an_unusable_name_is_refused_rather_than_passed_on()
    {
        var store = new ConfigurationSecretStore(new ConfigurationBuilder().Build());

        await Assert.ThrowsAsync<ArgumentException>(() => store.FindAsync("../elsewhere", TestContext.Current.CancellationToken));
    }
}

// What a deployment with no vault does: read what it was configured with, and say plainly that it cannot save
// anything rather than appear to.
public sealed class ConfigurationSecretStoreTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_configured_secret_is_found_and_an_unconfigured_one_is_simply_absent()
    {
        var store = StoreWith(("comgate-secret", "s3cret"));

        Assert.Equal("s3cret", await store.FindAsync("comgate-secret", CancellationToken));
        Assert.Null(await store.FindAsync("nothing-here", CancellationToken));
    }

    // Saving into configuration would look like it worked and be gone on the next restart, which is worse than
    // refusing: a merchant would believe their gateway was connected.
    [Fact]
    public async Task Saving_is_refused_rather_than_pretended()
    {
        var store = StoreWith();

        await Assert.ThrowsAsync<NotSupportedException>(() => store.SetAsync("comgate-secret", "s3cret", CancellationToken));
        await Assert.ThrowsAsync<NotSupportedException>(() => store.ForgetAsync("comgate-secret", CancellationToken));
    }

    private static ConfigurationSecretStore StoreWith(params (string Name, string Value)[] secrets) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(secrets.Select(secret =>
                new KeyValuePair<string, string?>($"{ConfigurationSecretStore.Section}:{secret.Name}", secret.Value)))
            .Build());
}

using System.Text;
using ShopForge.Shared.Security;

namespace ShopForge.UnitTests.Security;

public sealed class TotpTests
{
    // RFC 6238 publishes the seed "12345678901234567890" and the codes it produces at given times. If this
    // matches, an authenticator app will agree with us; if it does not, nothing else about the feature matters.
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void The_codes_match_the_ones_the_specification_publishes(long secondsSinceEpoch, string expected)
    {
        var key = Encoding.ASCII.GetBytes("12345678901234567890");

        Assert.Equal(expected, Totp.Code(key, secondsSinceEpoch / 30));
    }

    [Fact]
    public void A_code_from_the_right_secret_is_accepted()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;

        Assert.True(Totp.IsValid(secret, CodeAt(secret, now), now));
    }

    [Fact]
    public void A_code_from_a_different_secret_is_not()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(Totp.IsValid(Totp.NewSecret(), CodeAt(Totp.NewSecret(), now), now));
    }

    // A phone a step slow or fast is ordinary; a phone a minute out is somebody's clock to fix.
    [Theory]
    [InlineData(-30, true)]
    [InlineData(30, true)]
    [InlineData(-90, false)]
    [InlineData(90, false)]
    public void A_clock_slightly_out_still_works_and_one_badly_out_does_not(int secondsOut, bool expected)
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var code = CodeAt(secret, now.AddSeconds(secondsOut));

        Assert.Equal(expected, Totp.IsValid(secret, code, now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("12 456")]
    public void Anything_that_is_not_six_digits_is_refused_without_looking(string? code) =>
        Assert.False(Totp.IsValid(Totp.NewSecret(), code, DateTimeOffset.UtcNow));

    [Fact]
    public void A_secret_reads_back_as_the_alphabet_an_authenticator_expects()
    {
        var secret = Totp.NewSecret();

        Assert.Equal(32, secret.Length);
        Assert.All(secret, character => Assert.Contains(character, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
    }

    [Fact]
    public void The_enrolment_link_says_who_it_is_for()
    {
        var uri = Totp.EnrolmentUri("ShopForge", "owner@demo.local", "ABCDEFGHIJKLMNOP");

        Assert.StartsWith("otpauth://totp/ShopForge:owner%40demo.local?", uri, StringComparison.Ordinal);
        Assert.Contains("secret=ABCDEFGHIJKLMNOP", uri, StringComparison.Ordinal);
        Assert.Contains("issuer=ShopForge", uri, StringComparison.Ordinal);
    }

    // The code for that exact step, so a test about a clock being out is not quietly satisfied by the step next
    // door — which is what searching for "any code this moment accepts" did.
    private static string CodeAt(string secret, DateTimeOffset moment) => Totp.CodeAt(secret, moment);
}

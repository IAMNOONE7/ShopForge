using System.Net;
using ShopForge.Infrastructure.Provisioning;
using ShopForge.Shared.Provisioning;

namespace ShopForge.UnitTests.Provisioning;

// How long a provider asked to be left alone, read from its own headers rather than guessed, and which of its refusals
// stop an operation for a person (D-212).
public sealed class ProviderHeadersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Retry_after_in_seconds_is_waited_for_that_long()
    {
        using var response = Throttled();
        response.Headers.TryAddWithoutValidation("Retry-After", "120");

        Assert.Equal(TimeSpan.FromSeconds(120), ProviderHeaders.RetryAfter(response, Now));
    }

    [Fact]
    public void Retry_after_as_a_date_is_waited_for_until_then()
    {
        using var response = Throttled();
        response.Headers.TryAddWithoutValidation("Retry-After", Now.AddMinutes(3).ToString("R"));

        Assert.Equal(TimeSpan.FromMinutes(3), ProviderHeaders.RetryAfter(response, Now));
    }

    // Cloudflare's RateLimit header names each quota with what is left of it and when it comes back; the spent ones are
    // what the wait is for.
    [Theory]
    [InlineData("\"default\";r=0;t=30", 30)]
    [InlineData("\"default\";r=50;t=30, \"burst\";r=0;t=5", 5)]
    [InlineData("\"default\";r=0;t=30, \"burst\";r=0;t=45", 45)]
    [InlineData("\"default\";r=10;t=20", 20)]
    public void Cloudflare_s_ratelimit_header_says_when_a_spent_quota_comes_back(string header, int seconds)
    {
        using var response = Throttled();
        response.Headers.TryAddWithoutValidation("Ratelimit", header);

        Assert.Equal(TimeSpan.FromSeconds(seconds), ProviderHeaders.RetryAfter(response, Now));
    }

    // A header that says nothing usable leaves the wait to the caller's own rule.
    [Theory]
    [InlineData(null, null)]
    [InlineData("0", null)]
    [InlineData(null, "\"default\";r=0")]
    [InlineData(null, "\"default\";r=0;t=soon")]
    public void A_header_that_says_nothing_usable_is_no_wait(string? retryAfter, string? rateLimit)
    {
        using var response = Throttled();

        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }

        if (rateLimit is not null)
        {
            response.Headers.TryAddWithoutValidation("Ratelimit", rateLimit);
        }

        Assert.Null(ProviderHeaders.RetryAfter(response, Now));
    }

    [Theory]
    [InlineData(400, true, false)]
    [InlineData(401, true, false)]
    [InlineData(403, true, false)]
    [InlineData(404, true, false)]
    [InlineData(408, false, false)]
    [InlineData(409, false, false)]
    [InlineData(429, false, true)]
    [InlineData(500, false, false)]
    [InlineData(503, false, false)]
    public void A_refusal_a_provider_would_give_again_stops_and_a_rate_limit_is_waited_out(int status, bool permanent, bool throttled)
    {
        var refusal = new ProviderException("Cloudflare", status, "no", TimeSpan.FromSeconds(7));

        Assert.Equal((permanent, throttled), (refusal.IsPermanent, refusal.IsThrottled));
        Assert.Equal($"Cloudflare answered {status}: no", refusal.Message);
        Assert.Equal(TimeSpan.FromSeconds(7), refusal.RetryAfter);
    }

    // An adapter that knows a refusal will pass, such as a permission still on its way, says so over the status.
    [Fact]
    public void An_adapter_may_say_a_refusal_will_pass()
    {
        Assert.False(new ProviderException("Azure", 403, "not yet", permanent: false).IsPermanent);
        Assert.True(new ProviderException("Azure", 503, "never", permanent: true).IsPermanent);
    }

    private static HttpResponseMessage Throttled() => new(HttpStatusCode.TooManyRequests);
}

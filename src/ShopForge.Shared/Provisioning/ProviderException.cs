namespace ShopForge.Shared.Provisioning;

// A provider's no, or its not now. Not now carries how long the provider asked to be left alone, read from its own
// headers, which a step waits out without using up a try (D-212). An adapter that knows better than the status, such as
// one whose permission is still on its way, says whether the refusal is permanent.
public sealed class ProviderException(string provider, int status, string detail, TimeSpan? retryAfter = null, bool? permanent = null)
    : Exception($"{provider} answered {status}: {detail}")
{
    public string Provider { get; } = provider;

    public int Status { get; } = status;

    public TimeSpan? RetryAfter { get; } = retryAfter;

    public bool IsThrottled => Status == 429;

    // One it would give again: a 4xx other than a timeout, a conflict with work already under way, or a rate limit.
    public bool IsPermanent => permanent ?? (Status is >= 400 and < 500 and not (408 or 409 or 429));
}

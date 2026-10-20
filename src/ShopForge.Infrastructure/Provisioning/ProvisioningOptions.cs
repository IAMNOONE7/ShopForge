namespace ShopForge.Infrastructure.Provisioning;

// How the worker paces itself (D-210). A step's lease outlasts its timeout, so a step still running is never taken over;
// a step that fails is tried again after 30 seconds, doubling to half an hour, eight times in all, which is about an
// hour and a half before a person is asked. One that is waiting is looked at after a minute, doubling to an hour, for
// as long as it says it is waiting.
internal sealed record ProvisioningOptions(bool Enabled, TimeSpan Interval, TimeSpan Lease, TimeSpan StepTimeout)
{
    public const int Turns = 20;

    public int MaxAttempts { get; init; } = 8;

    public static ProvisioningOptions Default { get; } = new(true, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(60));

    public TimeSpan RetryAfter(int attempts) => Doubling(TimeSpan.FromSeconds(30), attempts, TimeSpan.FromMinutes(30));

    // A step may say when to look again, within six hours; otherwise the wait grows with each look.
    public TimeSpan CheckAfter(int checks, TimeSpan? asked) =>
        asked is { } wanted && wanted > TimeSpan.Zero
            ? (wanted < TimeSpan.FromHours(6) ? wanted : TimeSpan.FromHours(6))
            : Doubling(TimeSpan.FromMinutes(1), checks, TimeSpan.FromHours(1));

    private static TimeSpan Doubling(TimeSpan first, int count, TimeSpan longest)
    {
        var doublings = Math.Clamp(count - 1, 0, 20);
        var wait = first * Math.Pow(2, doublings);

        return wait < longest ? wait : longest;
    }
}

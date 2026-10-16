namespace ShopForge.Api.EndToEnd;

// The clock of a deployment whose time a journey may move on: the machine's time plus however far it has been moved, so
// a reservation can run out or a payment go unanswered without anybody waiting for it. Only forward, and no further
// than thirty days, well inside the key ring's ninety, so a signed-in browser stays signed in. Timers keep the
// machine's own time; only what the application reads as now moves (D-203).
internal sealed class ShiftedClock : TimeProvider
{
    public static readonly TimeSpan Furthest = TimeSpan.FromDays(30);

    private long _ticks;

    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;

    public bool TryAdvance(TimeSpan by)
    {
        if (by <= TimeSpan.Zero)
        {
            return false;
        }

        while (true)
        {
            var current = Interlocked.Read(ref _ticks);

            if (TimeSpan.FromTicks(current) + by > Furthest)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _ticks, current + by.Ticks, current) == current)
            {
                return true;
            }
        }
    }
}

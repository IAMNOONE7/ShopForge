using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Background;

// What each provider paid out, read a day at a time, so the reconciliation has the provider's side of the books
// (D-198). A day is read once and recorded, empty or not, and never asked for again. A store that has never started
// a payment through a provider has nothing to reconcile with it and is not asked.
internal sealed class StatementImport(
    DbContext dbContext,
    IStoreContext storeContext,
    IEnumerable<ISettlementStatements> statements,
    TimeProvider clock) : IStoreCatchUp
{
    // Comgate publishes a day's transfers the next morning. Reading a day two days after it ended keeps a slow
    // morning from being taken for a day with nothing in it — which, read once, it would stay.
    public const int DaysBehind = 2;

    // How far back a store's statements are read the first time.
    public const int FirstLook = 62;

    // A backlog is read across passes rather than in one, so one store's history does not hold up every other
    // store's catch-up.
    private const int DaysPerPass = 31;

    public string Name => "Settlement statements";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var read = 0;

        foreach (var provider in statements)
        {
            read += await ReadAsync(provider, cancellationToken);
        }

        return read;
    }

    private async Task<int> ReadAsync(ISettlementStatements provider, CancellationToken cancellationToken)
    {
        var firstAttempt = await dbContext.Set<PaymentAttempt>()
            .Where(attempt => attempt.Provider == provider.Key)
            .MinAsync(attempt => (DateTimeOffset?)attempt.StartedAt, cancellationToken);

        if (firstAttempt is null)
        {
            return 0;
        }

        var today = StatementDays.Today(provider.Zone, clock);
        var first = StatementDays.Of(provider.Zone, firstAttempt.Value);
        var start = first > today.AddDays(-FirstLook) ? first : today.AddDays(-FirstLook);
        var last = today.AddDays(-DaysBehind);
        var done = (await dbContext.Set<ProviderStatementDay>()
                .Where(day => day.Provider == provider.Key && day.Day >= start && day.Day <= last)
                .Select(day => day.Day)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var read = 0;

        for (var day = start; day <= last && read < DaysPerPass; day = day.AddDays(1))
        {
            if (done.Contains(day))
            {
                continue;
            }

            // No live account to read — never connected, a test account, or a secret gone — and nothing to wait
            // for: the next pass asks again.
            if (await provider.ReadDayAsync(day, cancellationToken) is not { } lines)
            {
                break;
            }

            // A day's lines and its record go in together, so a day is either read whole or not at all.
            var now = clock.GetUtcNow();
            var storeId = storeContext.StoreId!.Value;

            dbContext.AddRange(lines.Select(line => new ProviderStatementLine(storeId, provider.Key, day, line, now)));
            dbContext.Add(new ProviderStatementDay(storeId, provider.Key, day, lines.Count, now));
            await dbContext.SaveChangesAsync(cancellationToken);
            read++;
        }

        return read;
    }
}

// Days as a provider counts them. A payment ShopForge recorded at 23:30 UTC is already the next day in Prague, and it
// is counted on the day the provider's statement would show it.
internal static class StatementDays
{
    public static DateOnly Today(TimeZoneInfo zone, TimeProvider clock) => Of(zone, clock.GetUtcNow());

    public static DateOnly Of(TimeZoneInfo zone, DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    // The instant a day begins there, in UTC, which is the only offset the database takes. Midnight exists every day
    // in every zone a shop is in: clocks change in the small hours.
    public static DateTimeOffset Start(TimeZoneInfo zone, DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight)).ToUniversalTime();
    }
}

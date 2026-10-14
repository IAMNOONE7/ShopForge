using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Background;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Http;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Admin;

// What the provider paid out, laid beside what ShopForge recorded as paid through it, a day at a time, with every
// difference named and marked explained or not (D-198). It reads; it changes nothing. The two sides are matched on
// the provider's transaction, whatever day each happened to land on, and counted by day in the provider's zone.
internal static class AdminReconciliationEndpoints
{
    // A payment that is in no statement within this many days of statements read after it is a difference; until
    // then it is waited for. Comgate pays out on its own schedule, which a merchant can lengthen.
    public const int SettlementWait = 7;

    private const int LongestRange = 31;

    public static void MapAdminReconciliation(this IEndpointRouteBuilder storeAdmin) =>
        storeAdmin.MapGet("/reconciliation", GetAsync).RequireAuthorization(AdminPolicies.StoreManagement);

    private static async Task<Results<Ok<ReconciliationResponse>, ValidationProblem, NotFound>> GetAsync(
        string? provider,
        DateOnly? from,
        DateOnly? to,
        DbContext dbContext,
        IStoreContext storeContext,
        IEnumerable<ISettlementStatements> statements,
        IProviderConnections connections,
        IMerchantConnections merchants,
        IPaymentAttempts attempts,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var all = statements.ToList();
        var source = provider is null && all.Count == 1 ? all[0] : all.SingleOrDefault(candidate => candidate.Key == provider);

        if (source is null)
        {
            return TypedResults.NotFound();
        }

        var last = to ?? StatementDays.Today(source.Zone, clock);
        var first = from ?? last.AddDays(-13);
        var errors = new RequestErrors()
            .Check(first <= last, "from", "The first day comes before the last.")
            .Check(last.DayNumber - first.DayNumber < LongestRange, "to", $"A report covers at most {LongestRange} days.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var books = await Books.ReadAsync(dbContext, source, first, last, cancellationToken);
        var siblings = await SiblingsAsync(source.Key, storeContext, connections, merchants, cancellationToken);
        var differences = new List<ReconciliationDifference>();

        foreach (var payment in books.Paid)
        {
            differences.AddRange(Compare(payment, books, source.Zone));
        }

        foreach (var line in books.Lines)
        {
            if (await ExplainAsync(line, books, siblings, attempts, source.Key, cancellationToken) is { } difference)
            {
                differences.Add(difference);
            }
        }

        var days = Enumerable.Range(0, last.DayNumber - first.DayNumber + 1)
            .Select(offset => first.AddDays(offset))
            .Select(day => Day(day, books, differences, source.Zone))
            .ToList();
        var sorted = differences.OrderBy(difference => difference.Date).ThenBy(difference => difference.Kind, StringComparer.Ordinal).ToList();

        return TypedResults.Ok(new ReconciliationResponse(
            source.Key,
            source.Zone.Id,
            first,
            last,
            books.StatementsThrough,
            books.TestPayments,
            sorted.Count(difference => !difference.Explained),
            days,
            sorted));
    }

    // One payment ShopForge took: found in a statement for what was asked, found for something else, found more than
    // once, waited for, or missing.
    private static IEnumerable<ReconciliationDifference> Compare(PaymentAttempt payment, Books books, TimeZoneInfo zone)
    {
        var day = StatementDays.Of(zone, payment.ChangedAt);
        var settled = books.LinesOf(payment.Reference!).Where(line => line.IsPayment).ToList();
        var asked = payment.Currency.Code;

        if (books.Kept.TryGetValue(payment.Reference!, out var kept))
        {
            yield return Difference(
                day,
                "keptForRefund",
                explained: true,
                payment,
                settled.FirstOrDefault(),
                $"Taken for order {payment.OrderNumber}, which could not take it, and kept to give back ({Words(kept.Status)}).");
        }

        if (settled.Count > 1)
        {
            yield return Difference(
                day,
                "settledTwice",
                explained: false,
                payment,
                settled[0],
                $"Paid out {settled.Count} times for one payment.");
        }
        else if (settled.Count == 1)
        {
            var line = settled[0];

            if (line.Confirmed != payment.Amount || !string.Equals(line.Currency, asked, StringComparison.Ordinal))
            {
                yield return Difference(
                    day,
                    "amountDiffers",
                    explained: false,
                    payment,
                    line,
                    $"ShopForge asked for {Money(payment.Amount, asked)}; the statement confirms {Money(line.Confirmed, line.Currency)}.");
            }
        }
        else if (books.StatementsThrough is { } through && through.DayNumber - day.DayNumber >= SettlementWait)
        {
            yield return Difference(
                day,
                "notSettled",
                explained: false,
                payment,
                null,
                $"Paid on {Date(day)} and in no statement in the {SettlementWait} days after it.");
        }
        else
        {
            yield return Difference(
                day,
                "awaitingSettlement",
                explained: true,
                payment,
                null,
                books.StatementsThrough is { } read
                    ? $"Not in a statement yet: statements are read up to {Date(read)}, and a payment is waited for {SettlementWait} days."
                    : "Not in a statement yet: none has been read.");
        }
    }

    // One line of a statement that is not simply the settlement of a payment ShopForge recorded: something nobody has
    // written code for, something unreadable, money for an attempt ShopForge never saw paid, or money for nothing it
    // started. A line belonging to another store sold under the same account is that store's to reconcile.
    private static async Task<ReconciliationDifference?> ExplainAsync(
        ProviderStatementLine line,
        Books books,
        IReadOnlyCollection<Guid> siblings,
        IPaymentAttempts attempts,
        string provider,
        CancellationToken cancellationToken)
    {
        var day = line.PaidOn ?? line.Day;

        if (line.Problem is { } problem)
        {
            return Difference(day, "unreadableLine", explained: false, null, line, problem);
        }

        if (!line.IsPayment)
        {
            return Difference(
                day,
                "unrecognisedLine",
                explained: false,
                null,
                line,
                $"A line of {line.Kind}, which ShopForge does not read. Check it in the provider's own statement.");
        }

        if (books.Attempts.TryGetValue(line.TransactionId!, out var attempt))
        {
            return attempt.Status == PaymentAttemptStatus.Paid
                ? null
                : Difference(
                    day,
                    "settledButNotPaid",
                    explained: false,
                    attempt,
                    line,
                    $"Paid out, but ShopForge has the payment for order {attempt.OrderNumber} as {Words(attempt.Status)}.");
        }

        if (siblings.Count > 0 && await attempts.FindAsync(provider, line.TransactionId!, siblings, cancellationToken) is not null)
        {
            return null;
        }

        return Difference(day, "notStartedHere", explained: false, null, line, "No payment this store started has this transaction.");
    }

    private static ReconciliationDay Day(DateOnly day, Books books, List<ReconciliationDifference> differences, TimeZoneInfo zone)
    {
        var paid = books.Paid.Where(payment => StatementDays.Of(zone, payment.ChangedAt) == day).ToList();
        var settled = books.Lines.Where(line => line.IsPayment && line.PaidOn == day).ToList();

        return new ReconciliationDay(
            day,
            paid.Count,
            paid.Sum(payment => payment.Amount),
            settled.Count,
            settled.Sum(line => line.Confirmed ?? 0),
            settled.Sum(line => line.Fee ?? 0),
            settled.Sum(line => line.Transferred ?? 0),
            differences.Count(difference => difference.Date == day && !difference.Explained));
    }

    // The other stores sold under this store's account, whose payments come back on the same statement.
    private static async Task<IReadOnlyCollection<Guid>> SiblingsAsync(
        string provider,
        IStoreContext storeContext,
        IProviderConnections connections,
        IMerchantConnections merchants,
        CancellationToken cancellationToken)
    {
        if (await connections.FindAsync(provider, cancellationToken) is not { } connection)
        {
            return [];
        }

        var stores = await merchants.StoresAsync(provider, connection.MerchantId, cancellationToken);

        return [.. stores.Where(store => store != storeContext.StoreId)];
    }

    private static ReconciliationDifference Difference(
        DateOnly day,
        string kind,
        bool explained,
        PaymentAttempt? payment,
        ProviderStatementLine? line,
        string detail) =>
        new(
            day,
            kind,
            explained,
            payment?.OrderNumber,
            payment?.Reference ?? line?.TransactionId,
            payment?.Amount,
            line?.Confirmed,
            payment?.Currency.Code ?? line?.Currency,
            detail);

    private static string Money(decimal? amount, string? currency) =>
        amount is { } value ? $"{value.ToString("0.00", CultureInfo.InvariantCulture)} {currency}" : "no amount";

    private static string Date(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // "ClosedByHand" as "closed by hand": a status in a sentence a merchant reads.
    private static string Words(Enum status) =>
        string.Concat(status.ToString().Select((letter, index) =>
            index > 0 && char.IsUpper(letter) ? $" {char.ToLowerInvariant(letter)}" : $"{char.ToLowerInvariant(letter)}"));

    // Both sides of the books for the days asked about, and whatever the other side needs to be matched against,
    // read in a handful of queries.
    private sealed record Books(
        List<PaymentAttempt> Paid,
        List<ProviderStatementLine> Lines,
        ILookup<string, ProviderStatementLine> ByTransaction,
        Dictionary<string, PaymentAttempt> Attempts,
        Dictionary<string, UnappliedPayment> Kept,
        DateOnly? StatementsThrough,
        int TestPayments)
    {
        public IEnumerable<ProviderStatementLine> LinesOf(string transactionId) => ByTransaction[transactionId];

        public static async Task<Books> ReadAsync(
            DbContext dbContext,
            ISettlementStatements source,
            DateOnly first,
            DateOnly last,
            CancellationToken cancellationToken)
        {
            var key = source.Key;
            var start = StatementDays.Start(source.Zone, first);
            var end = StatementDays.Start(source.Zone, last.AddDays(1));

            // Live payments only: Comgate settles nothing else, so a test payment would be a difference that is none.
            var paidInRange = dbContext.Set<PaymentAttempt>().AsNoTracking()
                .Where(attempt => attempt.Provider == key
                    && attempt.Status == PaymentAttemptStatus.Paid
                    && attempt.Reference != null
                    && attempt.ChangedAt >= start
                    && attempt.ChangedAt < end);
            var paid = await paidInRange.Where(attempt => attempt.Environment == "live").ToListAsync(cancellationToken);
            var testPayments = await paidInRange.CountAsync(attempt => attempt.Environment == "test", cancellationToken);

            var lines = await dbContext.Set<ProviderStatementLine>().AsNoTracking()
                .Where(line => line.Provider == key
                    && ((line.PaidOn != null && line.PaidOn >= first && line.PaidOn <= last)
                        || (line.PaidOn == null && line.Day >= first && line.Day <= last)))
                .ToListAsync(cancellationToken);

            var references = paid.Select(payment => payment.Reference!)
                .Concat(lines.Where(line => line.TransactionId != null).Select(line => line.TransactionId!))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var matching = await dbContext.Set<ProviderStatementLine>().AsNoTracking()
                .Where(line => line.Provider == key && line.TransactionId != null && references.Contains(line.TransactionId))
                .ToListAsync(cancellationToken);
            var attempts = await dbContext.Set<PaymentAttempt>().AsNoTracking()
                .Where(attempt => attempt.Provider == key && attempt.Reference != null && references.Contains(attempt.Reference))
                .ToDictionaryAsync(attempt => attempt.Reference!, StringComparer.Ordinal, cancellationToken);
            var kept = await dbContext.Set<UnappliedPayment>().AsNoTracking()
                .Where(payment => payment.Provider == key && payment.Reference != null && references.Contains(payment.Reference))
                .ToDictionaryAsync(payment => payment.Reference!, StringComparer.Ordinal, cancellationToken);
            var through = await dbContext.Set<ProviderStatementDay>()
                .Where(day => day.Provider == key)
                .MaxAsync(day => (DateOnly?)day.Day, cancellationToken);

            return new Books(
                paid,
                lines,
                matching.ToLookup(line => line.TransactionId!, StringComparer.Ordinal),
                attempts,
                kept,
                through,
                testPayments);
        }
    }
}

// `TestPayments` counts payments taken in the provider's test environment in the range: never settled, so left out
// rather than reported missing.
internal sealed record ReconciliationResponse(
    string Provider,
    string TimeZone,
    DateOnly From,
    DateOnly To,
    DateOnly? StatementsThrough,
    int TestPayments,
    int Unexplained,
    List<ReconciliationDay> Days,
    List<ReconciliationDifference> Differences);

internal sealed record ReconciliationDay(
    DateOnly Date,
    int Paid,
    decimal PaidAmount,
    int Settled,
    decimal SettledAmount,
    decimal Fees,
    decimal Transferred,
    int Unexplained);

internal sealed record ReconciliationDifference(
    DateOnly Date,
    string Kind,
    bool Explained,
    string? OrderNumber,
    string? TransactionId,
    decimal? Expected,
    decimal? Settled,
    string? Currency,
    string Detail);

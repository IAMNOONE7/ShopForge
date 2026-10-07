using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Payments;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Payments;

namespace ShopForge.Orders.Background;

// When nobody called. A notification can be lost — a deploy mid-flight, a network that swallowed it, a
// provider that gave up retrying — and a shopper who paid is owed their order either way, so the shop goes and
// asks (D-176).
//
// What comes back takes the same path a push takes, including its event id, so a sweep and a late push that
// cross in the post land one effect between them.
internal sealed class PaymentReconciliation(
    DbContext dbContext,
    IEnumerable<IPaymentEnquiries> enquiries,
    PaymentResults results,
    TimeProvider clock) : IStoreCatchUp
{
    // Long enough that an attempt is not asked about while the shopper is still on the gateway's page, short
    // enough that a lost notification is noticed while they are still watching their order.
    public static readonly TimeSpan Unanswered = TimeSpan.FromMinutes(15);

    // Past this, nobody is waiting and the gateway has long since decided. An attempt this old is left as it
    // is rather than asked about forever.
    public static readonly TimeSpan GivenUp = TimeSpan.FromDays(7);

    public string Name => "Payment reconciliation";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var providers = enquiries.ToDictionary(enquiry => enquiry.Key, StringComparer.Ordinal);

        if (providers.Count == 0)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var waiting = await dbContext.Set<PaymentAttempt>()
            .AsNoTracking()
            .Where(attempt =>
                attempt.Reference != null
                && !(attempt.Status == PaymentAttemptStatus.Paid || attempt.Status == PaymentAttemptStatus.Failed)
                && attempt.ChangedAt <= now - Unanswered
                && attempt.StartedAt >= now - GivenUp
                && providers.Keys.Contains(attempt.Provider))
            .OrderBy(attempt => attempt.StartedAt)
            .Select(attempt => new Waiting(
                attempt.Provider,
                new PaymentEnquiry(attempt.StoreId, attempt.OrderNumber, attempt.Reference!, attempt.Amount, attempt.Currency)))
            .ToListAsync(cancellationToken);

        var settled = 0;

        foreach (var (provider, enquiry) in waiting)
        {
            // A provider that cannot say leaves the attempt alone to be asked again. Only an answer counts.
            if (await providers[provider].AskAsync(enquiry, cancellationToken) is not { } answer)
            {
                continue;
            }

            if (await results.RecordAsync(provider, answer, cancellationToken) == PaymentRecord.Recorded)
            {
                settled++;
            }
        }

        return settled;
    }

    private sealed record Waiting(string Provider, PaymentEnquiry Enquiry);
}

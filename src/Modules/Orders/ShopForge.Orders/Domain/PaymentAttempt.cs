using ShopForge.Shared.Payments;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

// One try at paying for an order. A shopper who abandons a payment and starts again makes two of these, so the
// first one's story survives the second: what was sent, where they were sent, and what became of it. The order
// keeps naming the payment that actually succeeded; this is where the ones that did not are remembered (D-141).
internal sealed class PaymentAttempt : IStoreOwned
{
    private PaymentAttempt()
    {
    }

    public PaymentAttempt(
        Guid storeId,
        string orderNumber,
        string provider,
        string? reference,
        decimal amount,
        Currency currency,
        string? redirectUrl,
        DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        OrderNumber = orderNumber;
        Provider = provider;
        Reference = reference;
        Amount = amount;
        Currency = currency;
        RedirectUrl = redirectUrl;
        Status = PaymentAttemptStatus.Started;
        StartedAt = startedAt;
        ChangedAt = startedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string OrderNumber { get; private set; } = null!;

    public string Provider { get; private set; } = null!;

    // What the provider calls this attempt. Null for a method the store settles itself, which has nothing to call
    // it; unique per store and provider for everyone else, so a callback can find its own attempt and no other.
    public string? Reference { get; private set; }

    // What was asked for, as it was sent: a later price change must not rewrite what somebody was charged.
    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; } = null!;

    // Exactly what the provider gave back, unmodified — the shopper was sent there and nowhere else.
    public string? RedirectUrl { get; private set; }

    public PaymentAttemptStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    // Which of the provider's environments this went through — "test" or "live" — learned the first time
    // anybody hears back about the attempt, because that is the first moment a connection is read for it. Null
    // for a method the store settles itself, which has no gateway to be in either environment of (D-176).
    public string? Environment { get; private set; }

    public void Went(string environment) => Environment = environment;

    public bool IsFinished => Status is PaymentAttemptStatus.Paid or PaymentAttemptStatus.Failed;

    // An attempt that has finished stays finished: a late or duplicated message about it changes nothing, the
    // way a paid order ignores a second payment (D-058, D-130).
    public bool Record(PaymentAttemptStatus status, DateTimeOffset at)
    {
        if (IsFinished || status == Status)
        {
            return false;
        }

        Status = status;
        ChangedAt = at;

        return true;
    }
}

internal enum PaymentAttemptStatus
{
    Started,
    Pending,
    Authorized,
    Paid,
    Failed,
}

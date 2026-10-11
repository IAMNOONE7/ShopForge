using ShopForge.Shared.Payments;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

// Money a provider took that its order could not take: the order had been cancelled before the payment landed, or
// had already been paid with other money. It is not applied to the order, because the order is past wanting it,
// but it is the store's to give back — and until somebody has, it is kept and shown rather than logged and
// forgotten, which is what used to happen to it (D-193).
internal sealed class UnappliedPayment : IStoreOwned
{
    public const int MaxNoteLength = 500;

    private UnappliedPayment()
    {
    }

    public UnappliedPayment(
        Guid storeId,
        string orderNumber,
        string provider,
        string? reference,
        decimal amount,
        Currency currency,
        UnappliedReason reason,
        DateTimeOffset receivedAt)
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
        Reason = reason;
        Status = UnappliedPaymentStatus.Open;
        ReceivedAt = receivedAt;
        ChangedAt = receivedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string OrderNumber { get; private set; } = null!;

    public string Provider { get; private set; } = null!;

    // What the provider calls the money, which is what a refund has to name. Null only where a provider gave
    // none; that money can only be given back at the provider, and the record is then closed by hand.
    public string? Reference { get; private set; }

    // What the attempt asked for, which is what the provider's own answer was checked against (D-141).
    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; } = null!;

    public UnappliedReason Reason { get; private set; }

    public UnappliedPaymentStatus Status { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    // Why somebody closed it without a refund going through here — given back at the provider's own portal, say,
    // or settled with the customer another way. It is required for that, so the reason is on the record.
    public string? Note { get; private set; }

    // Open, or a refund whose outcome nobody heard: once somebody has checked at the provider, either can be
    // closed by hand. Anything already settled stays settled.
    public bool CloseByHand(string note, DateTimeOffset at)
    {
        if (Status is not (UnappliedPaymentStatus.Open or UnappliedPaymentStatus.Refunding))
        {
            return false;
        }

        Status = UnappliedPaymentStatus.ClosedByHand;
        Note = note;
        ChangedAt = at;

        return true;
    }
}

internal enum UnappliedReason
{
    // Cancelled before the money arrived: its reservation ran out, or the store cancelled it.
    OrderCancelled,

    // Already paid with other money, so this is a second payment for the same goods.
    OrderAlreadyPaid,
}

internal enum UnappliedPaymentStatus
{
    Open,

    // A refund was asked for and the provider never said how it went. Refunding again could give the money back
    // twice, so it waits for somebody to look at the provider and close it by hand.
    Refunding,

    Refunded,

    ClosedByHand,
}

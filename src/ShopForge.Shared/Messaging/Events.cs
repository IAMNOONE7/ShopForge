namespace ShopForge.Shared.Messaging;

public sealed record OrderPlaced(string OrderNumber, string Email, decimal GrandTotal, string Currency, string PaymentInstructions) : IDomainEvent
{
    public static string EventType => "order.placed";
}

public sealed record PaymentReceived(string OrderNumber, string Email, decimal GrandTotal, string Currency) : IDomainEvent
{
    public static string EventType => "order.paid";
}

public sealed record OrderCancelled(string OrderNumber, string Email, string Reason) : IDomainEvent
{
    public static string EventType => "order.cancelled";
}

public sealed record ShipmentCreated(string OrderNumber, string Email, string Carrier, string TrackingNumber, string? PickupPoint) : IDomainEvent
{
    public static string EventType => "order.shipped";
}

public sealed record ReturnDecided(string OrderNumber, string Email, string ReturnNumber, bool Accepted) : IDomainEvent
{
    public static string EventType => "return.decided";
}

public sealed record ReturnRefunded(string OrderNumber, string Email, string ReturnNumber, decimal Amount, string Currency) : IDomainEvent
{
    public static string EventType => "return.refunded";
}

// Money that reached the store after its order was cancelled, or a second time for an order already paid, given
// back (D-193).
public sealed record UnappliedPaymentRefunded(string OrderNumber, string Email, decimal Amount, string Currency, bool OrderWasCancelled) : IDomainEvent
{
    public static string EventType => "payment.unapplied-refunded";
}

public sealed record EmailRequested(string To, string Subject, string Body, string? AttachmentReference = null) : IDomainEvent
{
    public static string EventType => "email.requested";
}

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

public sealed record EmailRequested(string To, string Subject, string Body) : IDomainEvent
{
    public static string EventType => "email.requested";
}

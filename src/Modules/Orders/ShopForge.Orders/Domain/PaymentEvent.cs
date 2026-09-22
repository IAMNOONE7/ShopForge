using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

// Providers retry until they get a 200, so every event they send is recorded once and ignored afterwards (D-058).
internal sealed class PaymentEvent : IStoreOwned
{
    private PaymentEvent()
    {
    }

    public PaymentEvent(Guid storeId, string provider, string eventId, string orderNumber, DateTimeOffset receivedAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Provider = provider;
        EventId = eventId;
        OrderNumber = orderNumber;
        ReceivedAt = receivedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Provider { get; private set; } = null!;

    public string EventId { get; private set; } = null!;

    public string OrderNumber { get; private set; } = null!;

    public DateTimeOffset ReceivedAt { get; private set; }
}

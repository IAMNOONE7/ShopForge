namespace ShopForge.Shared.Diagnostics;

// The few numbers a shop owner would ask about. Modules count through this so business code stays free of the
// metrics API, and so the counters are easy to assert on in a test.
public interface IShopForgeMetrics
{
    void OrderPlaced(string paymentMethod);

    void PaymentConfirmed(string provider);

    // Money a provider took that its order could not take. Every one of these is a refund somebody owes, so
    // it is the number an alert watches (D-193).
    void PaymentUnapplied(string provider, string reason);

    void OrderCancelled(string reason);

    void OrderRefunded(string reason);

    void ShipmentCreated();

    void ReservationRefused();
}

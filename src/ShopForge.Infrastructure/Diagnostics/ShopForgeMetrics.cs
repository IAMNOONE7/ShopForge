using System.Diagnostics;
using System.Diagnostics.Metrics;
using ShopForge.Shared.Diagnostics;

namespace ShopForge.Infrastructure.Diagnostics;

internal sealed class ShopForgeMetrics : IShopForgeMetrics, IDisposable
{
    public const string MeterName = "ShopForge";
    public const string ActivitySourceName = "ShopForge";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private readonly Meter _meter;
    private readonly Counter<long> _ordersPlaced;
    private readonly Counter<long> _paymentsConfirmed;
    private readonly Counter<long> _ordersCancelled;
    private readonly Counter<long> _shipments;
    private readonly Counter<long> _reservationsRefused;

    public ShopForgeMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        _ordersPlaced = _meter.CreateCounter<long>("shopforge.orders.placed", description: "Orders placed by shoppers.");
        _paymentsConfirmed = _meter.CreateCounter<long>("shopforge.payments.confirmed", description: "Payments confirmed for orders.");
        _ordersCancelled = _meter.CreateCounter<long>("shopforge.orders.cancelled", description: "Orders cancelled, by reason.");
        _shipments = _meter.CreateCounter<long>("shopforge.shipments.created", description: "Shipments handed to a carrier.");
        _reservationsRefused = _meter.CreateCounter<long>("shopforge.stock.reservations_refused", description: "Checkouts refused for lack of stock.");
    }

    public void OrderPlaced(string paymentMethod) => _ordersPlaced.Add(1, new KeyValuePair<string, object?>("payment.method", paymentMethod));

    public void PaymentConfirmed(string provider) => _paymentsConfirmed.Add(1, new KeyValuePair<string, object?>("payment.provider", provider));

    public void OrderCancelled(string reason) => _ordersCancelled.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void ShipmentCreated() => _shipments.Add(1);

    public void ReservationRefused() => _reservationsRefused.Add(1);

    public void Dispose() => _meter.Dispose();
}

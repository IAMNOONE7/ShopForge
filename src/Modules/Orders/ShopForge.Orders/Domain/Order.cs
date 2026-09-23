using ShopForge.Shared.Shipping;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

internal sealed class Order : IStoreOwned
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
    }

    public Order(
        Guid storeId,
        string number,
        string currency,
        string email,
        Address billing,
        Address shipping,
        ChosenMethods methods,
        ChosenPickupPoint? pickupPoint,
        DateTimeOffset placedAt,
        DateTimeOffset reservationExpiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Number = number;
        AccessToken = Guid.CreateVersion7();
        Currency = currency;
        Email = email.Trim().ToLowerInvariant();
        BillingAddress = billing;
        ShippingAddress = shipping;
        PaymentMethodCode = methods.PaymentCode;
        PaymentMethodName = methods.PaymentName;
        PaymentProviderKey = methods.PaymentProviderKey;
        ShippingMethodCode = methods.ShippingCode;
        ShippingMethodName = methods.ShippingName;
        ShippingPrice = methods.ShippingPrice;
        ShippingVatRate = methods.ShippingVatRate;
        PickupPointCode = pickupPoint?.Code;
        PickupPointName = pickupPoint?.Name;
        PickupPointAddress = pickupPoint?.Address;
        Status = OrderStatus.AwaitingPayment;
        PlacedAt = placedAt;
        ReservationExpiresAt = reservationExpiresAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Number { get; private set; } = null!;

    public Guid AccessToken { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    // Until this moment the order holds stock; after it, an unpaid order is cancelled and the stock goes back (D-048).
    public DateTimeOffset ReservationExpiresAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    // What the provider calls the payment, so it can be refunded later (D-081).
    public string? PaymentReference { get; private set; }

    // Null for a guest order; set at checkout or when the customer proves the e-mail it was placed with (D-054).
    public Guid? StoreCustomerId { get; private set; }

    public string Email { get; private set; } = null!;

    public string Currency { get; private set; } = null!;

    public Address BillingAddress { get; private set; } = null!;

    public Address ShippingAddress { get; private set; } = null!;

    public string PaymentMethodCode { get; private set; } = null!;

    public string PaymentMethodName { get; private set; } = null!;

    // Which provider took the money, so a refund knows who to ask (D-081).
    public string PaymentProviderKey { get; private set; } = null!;

    public string ShippingMethodCode { get; private set; } = null!;

    public string ShippingMethodName { get; private set; } = null!;

    public decimal ShippingPrice { get; private set; }

    public decimal ShippingVatRate { get; private set; }

    public string? PickupPointCode { get; private set; }

    public string? PickupPointName { get; private set; }

    public Address? PickupPointAddress { get; private set; }

    public Shipment? Shipment { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public decimal ItemsTotal => _lines.Sum(line => line.LineTotal);

    public decimal GrandTotal => ItemsTotal + ShippingPrice;

    public decimal VatTotal => _lines.Sum(line => line.VatAmount) + Money.VatOf(ShippingPrice, ShippingVatRate);

    public void AddLine(Guid storeProductId, string name, decimal unitPrice, decimal vatRate, int quantity)
    {
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "An order line needs at least one item.");
        }

        _lines.Add(new OrderLine(storeProductId, name, unitPrice, vatRate, quantity));
    }

    public void AssignTo(Guid storeCustomerId) => StoreCustomerId = storeCustomerId;

    public bool ConfirmPayment(DateTimeOffset paidAt, string? paymentReference = null)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            return false;
        }

        Status = OrderStatus.Paid;
        PaidAt = paidAt;
        PaymentReference = paymentReference;

        return true;
    }

    // Money and goods both go back, so an order that was never paid cannot be refunded (D-081).
    public bool Refund()
    {
        if (Status is not (OrderStatus.Paid or OrderStatus.Shipped))
        {
            return false;
        }

        Status = OrderStatus.Refunded;

        return true;
    }

    public bool Cancel()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            return false;
        }

        Status = OrderStatus.Cancelled;

        return true;
    }

    // One shipment per order, and only once it is paid for (D-064).
    public bool Ship(ShipmentDetails details, DateTimeOffset shippedAt)
    {
        if (Status != OrderStatus.Paid)
        {
            return false;
        }

        Shipment = new Shipment(details.Carrier, details.TrackingNumber, details.TrackingUrl, shippedAt);
        Status = OrderStatus.Shipped;

        return true;
    }
}

internal sealed class Shipment
{
    private Shipment()
    {
    }

    internal Shipment(string carrier, string trackingNumber, string? trackingUrl, DateTimeOffset shippedAt)
    {
        Carrier = carrier;
        TrackingNumber = trackingNumber;
        TrackingUrl = trackingUrl;
        ShippedAt = shippedAt;
    }

    public string Carrier { get; private set; } = null!;

    public string TrackingNumber { get; private set; } = null!;

    public string? TrackingUrl { get; private set; }

    public DateTimeOffset ShippedAt { get; private set; }
}

internal sealed class OrderLine
{
    private OrderLine()
    {
    }

    internal OrderLine(Guid storeProductId, string name, decimal unitPrice, decimal vatRate, int quantity)
    {
        StoreProductId = storeProductId;
        ProductName = name;
        UnitPrice = unitPrice;
        VatRate = vatRate;
        Quantity = quantity;
    }

    public Guid StoreProductId { get; private set; }

    public string ProductName { get; private set; } = null!;

    public decimal UnitPrice { get; private set; }

    public decimal VatRate { get; private set; }

    public int Quantity { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;

    public decimal VatAmount => Money.VatOf(LineTotal, VatRate);
}

internal enum OrderStatus
{
    AwaitingPayment,
    Paid,
    Shipped,
    Cancelled,
    Refunded,
}

internal sealed record ChosenPickupPoint(string Code, string Name, Address Address);

internal sealed record ChosenMethods(
    string PaymentCode,
    string PaymentName,
    string PaymentProviderKey,
    string ShippingCode,
    string ShippingName,
    decimal ShippingPrice,
    decimal ShippingVatRate);

// Prices are stored with VAT included (D-044), so the tax part is derived from the gross amount.
internal static class Money
{
    public static decimal VatOf(decimal gross, decimal vatRate) =>
        decimal.Round(gross - (gross / (1 + (vatRate / 100m))), 2, MidpointRounding.AwayFromZero);
}

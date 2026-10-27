using ShopForge.Shared.Payments;
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
        Currency currency,
        string email,
        string phone,
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
        Phone = phone.Trim();
        BillingAddress = billing;
        ShippingAddress = shipping;
        PaymentMethodCode = methods.PaymentCode;
        PaymentMethodName = methods.PaymentName;
        PaymentProviderKey = methods.PaymentProviderKey;
        ShippingProviderKey = methods.ShippingProviderKey;
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

    // Every order carries one, whatever the method: a rule that depended on the carrier would break the day a
    // merchant switched method (D-144). Null only on orders placed before it was asked for.
    public string? Phone { get; private set; }

    public Currency Currency { get; private set; } = null!;

    public Address BillingAddress { get; private set; } = null!;

    public Address ShippingAddress { get; private set; } = null!;

    public string PaymentMethodCode { get; private set; } = null!;

    public string PaymentMethodName { get; private set; } = null!;

    // Which provider took the money, so a refund knows who to ask (D-081).
    public string PaymentProviderKey { get; private set; } = null!;

    // Which carrier the order was placed with, read from the method at the time rather than looked up again:
    // a method that is renamed or deactivated afterwards must not change how an old order ships (D-081).
    public string? ShippingProviderKey { get; private set; }

    public string ShippingMethodCode { get; private set; } = null!;

    public string ShippingMethodName { get; private set; } = null!;

    public decimal ShippingPrice { get; private set; }

    public decimal ShippingVatRate { get; private set; }

    public string? PickupPointCode { get; private set; }

    public string? PickupPointName { get; private set; }

    public Address? PickupPointAddress { get; private set; }

    public Shipment? Shipment { get; private set; }

    public string? DiscountCode { get; private set; }

    public string? DiscountName { get; private set; }

    // What the code actually took off, lines and shipping together (D-087).
    public decimal DiscountTotal { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public decimal ItemsTotal => _lines.Sum(line => line.LineTotal);

    public decimal ShippingCharged => ShippingPrice - ShippingDiscount;

    public decimal GrandTotal => ItemsTotal + ShippingCharged;

    public decimal VatTotal => _lines.Sum(line => line.VatIn(Currency)) + Money.VatOf(ShippingCharged, ShippingVatRate, Currency);

    public decimal ShippingDiscount { get; private set; }

    // What has gone back to the customer so far, across every return of this order.
    public decimal RefundedTotal { get; private set; }

    public void AddLine(Guid storeProductId, Guid variantId, string name, decimal unitPrice, decimal vatRate, int quantity, decimal discount = 0m)
    {
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "An order line needs at least one item.");
        }

        _lines.Add(new OrderLine(storeProductId, variantId, name, unitPrice, vatRate, quantity, discount));
    }

    public void ApplyDiscount(string code, string name, decimal shippingDiscount)
    {
        DiscountCode = code;
        DiscountName = name;
        ShippingDiscount = shippingDiscount;
        DiscountTotal = _lines.Sum(line => line.Discount) + shippingDiscount;
    }

    public void AssignTo(Guid storeCustomerId) => StoreCustomerId = storeCustomerId;

    // What the order is worth, what VAT it carried and which documents it produced are the accounting record and
    // stay exactly as they were. Who bought it does not (D-117).
    public void Anonymise()
    {
        Email = PersonalData.Erased;
        BillingAddress = PersonalData.ErasedAddress;
        ShippingAddress = PersonalData.ErasedAddress;
        PickupPointAddress = PickupPointAddress is null ? null : PersonalData.ErasedAddress;
        StoreCustomerId = null;
    }

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

    // Money and goods both go back, so an order that was never paid cannot be refunded (D-081). A return gives back
    // part of an order, so the order counts what it has paid back and is refunded only once that is everything (D-096).
    public bool RecordRefund(decimal amount)
    {
        if (Status is not (OrderStatus.Paid or OrderStatus.Shipped))
        {
            return false;
        }

        RefundedTotal += amount;

        if (RefundedTotal >= GrandTotal)
        {
            Status = OrderStatus.Refunded;
        }

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
    public const int MaxProductNameLength = 200;

    private OrderLine()
    {
    }

    internal OrderLine(Guid storeProductId, Guid variantId, string name, decimal unitPrice, decimal vatRate, int quantity, decimal discount)
    {
        StoreProductId = storeProductId;
        VariantId = variantId;
        ProductName = name;
        UnitPrice = unitPrice;
        VatRate = vatRate;
        Quantity = quantity;
        Discount = discount;
    }

    public Guid StoreProductId { get; private set; }

    // Which form of it was sold: what the reservation took and what a return puts back (D-135).
    public Guid VariantId { get; private set; }

    public string ProductName { get; private set; } = null!;

    public decimal UnitPrice { get; private set; }

    public decimal VatRate { get; private set; }

    public int Quantity { get; private set; }

    // The share of the order's discount this line carried (D-085).
    public decimal Discount { get; private set; }

    public decimal LineTotal => (UnitPrice * Quantity) - Discount;

    // A line's tax is only money inside its order, which is what knows the currency it rounds in (D-186).
    public decimal VatIn(Currency currency) => Money.VatOf(LineTotal, VatRate, currency);
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
    string ShippingProviderKey,
    string ShippingCode,
    string ShippingName,
    decimal ShippingPrice,
    decimal ShippingVatRate);

// Prices are stored with VAT included (D-044), so the tax part is derived from the gross amount, and rounded to
// the places the money it is charged in actually has (D-186).
internal static class Money
{
    public static decimal VatOf(decimal gross, decimal vatRate, Currency currency) =>
        currency.Round(gross - (gross / (1 + (vatRate / 100m))));
}

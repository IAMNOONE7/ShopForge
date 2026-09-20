using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

internal sealed class Order : IStoreOwned
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
    }

    public Order(Guid storeId, string number, string currency, string email, Address billing, Address shipping, ChosenMethods methods, DateTimeOffset placedAt)
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
        ShippingMethodCode = methods.ShippingCode;
        ShippingMethodName = methods.ShippingName;
        ShippingPrice = methods.ShippingPrice;
        ShippingVatRate = methods.ShippingVatRate;
        Status = OrderStatus.Placed;
        PlacedAt = placedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Number { get; private set; } = null!;

    public Guid AccessToken { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public string Email { get; private set; } = null!;

    public string Currency { get; private set; } = null!;

    public Address BillingAddress { get; private set; } = null!;

    public Address ShippingAddress { get; private set; } = null!;

    public string PaymentMethodCode { get; private set; } = null!;

    public string PaymentMethodName { get; private set; } = null!;

    public string ShippingMethodCode { get; private set; } = null!;

    public string ShippingMethodName { get; private set; } = null!;

    public decimal ShippingPrice { get; private set; }

    public decimal ShippingVatRate { get; private set; }

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
    Placed,
    Cancelled,
}

internal sealed record ChosenMethods(
    string PaymentCode,
    string PaymentName,
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

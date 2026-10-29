using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Shipping;

namespace ShopForge.UnitTests.Orders;

public sealed class OrderTests
{
    private static readonly Address Address = new("Alex Buyer", "1 Main Street", null, "Dublin", "D01 AB12", "IE");
    private static readonly ChosenMethods Methods = new("bank-transfer", "Bank transfer", "manual", "store", "courier", "Courier", 4.90m, 21m);
    private static readonly DateTimeOffset PlacedAt = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_order_waits_for_payment_and_holds_stock_until_its_expiry()
    {
        var order = Place();

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(PlacedAt.AddMinutes(30), order.ReservationExpiresAt);
        Assert.Null(order.PaidAt);
    }

    [Fact]
    public void Payment_is_confirmed_once()
    {
        var order = Place();

        Assert.True(order.ConfirmPayment(PlacedAt.AddMinutes(5)));
        Assert.False(order.ConfirmPayment(PlacedAt.AddMinutes(6)));
        Assert.Equal(PlacedAt.AddMinutes(5), order.PaidAt);
    }

    [Fact]
    public void A_paid_order_cannot_be_cancelled()
    {
        var order = Place();
        order.ConfirmPayment(PlacedAt);

        Assert.False(order.Cancel());
        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void Totals_add_shipping_and_derive_vat_from_gross_prices()
    {
        var order = Place();
        order.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Oak Chair", 121m, 21m, 2);

        Assert.Equal(242m, order.ItemsTotal);
        Assert.Equal(246.90m, order.GrandTotal);
        Assert.Equal(42.85m, order.VatTotal);
    }

    [Fact]
    public void Only_a_paid_order_can_be_shipped()
    {
        var unpaid = Place();
        var paid = Place();
        paid.ConfirmPayment(PlacedAt);

        Assert.False(unpaid.Ship(Parcel, PlacedAt.AddHours(1)));
        Assert.True(paid.Ship(Parcel, PlacedAt.AddHours(1)));
        Assert.False(paid.Ship(Parcel, PlacedAt.AddHours(2)));
        Assert.Equal((OrderStatus.Shipped, "PKG-1"), (paid.Status, paid.Shipment!.TrackingNumber));
    }

    // Kept as a free-shipping code is, but never counted as one (D-221).
    [Fact]
    public void Free_delivery_keeps_the_price_and_charges_nothing_for_it()
    {
        var order = Place();
        order.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Oak Chair", 121m, 21m, 2);

        order.MakeDeliveryFree(200m);

        Assert.Equal((4.90m, 4.90m, 0m), (order.ShippingPrice, order.ShippingDiscount, order.ShippingCharged));
        Assert.Equal((242m, 42m), (order.GrandTotal, order.VatTotal));
        Assert.Equal((200m, 0m, (string?)null), (order.FreeShippingThreshold, order.DiscountTotal, order.DiscountCode));
    }

    [Fact]
    public void Free_delivery_beside_a_code_off_the_goods_is_not_the_codes()
    {
        var order = Place();
        order.AddLine(Guid.NewGuid(), Guid.NewGuid(), "Oak Chair", 121m, 21m, 2, discount: 24.20m);
        order.ApplyDiscount("TEN", "Ten off", shippingDiscount: 0m);

        order.MakeDeliveryFree(200m);

        Assert.Equal((24.20m, 4.90m, 217.80m), (order.DiscountTotal, order.ShippingDiscount, order.GrandTotal));
    }

    [Fact]
    public void A_code_that_took_delivery_off_leaves_nothing_for_the_threshold()
    {
        var order = Place();
        order.ApplyDiscount("SHIPFREE", "Free delivery", shippingDiscount: 4.90m);

        Assert.Throws<InvalidOperationException>(() => order.MakeDeliveryFree(200m));
        Assert.Null(order.FreeShippingThreshold);
    }

    private static readonly ShipmentDetails Parcel = new("Courier", "PKG-1", null);

    private static Order Place() => new(
        Guid.NewGuid(),
        "2026-00001",
        Currency.Of("EUR"),
        "buyer@example.test",
        "+420 123 456 789",
        Address,
        Address,
        Methods,
        pickupPoint: null,
        PlacedAt,
        PlacedAt.AddMinutes(30));
}

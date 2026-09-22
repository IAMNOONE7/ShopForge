using ShopForge.Orders.Domain;
using ShopForge.Shared.Shipping;

namespace ShopForge.UnitTests.Orders;

public sealed class OrderTests
{
    private static readonly Address Address = new("Alex Buyer", "1 Main Street", null, "Dublin", "D01 AB12", "IE");
    private static readonly ChosenMethods Methods = new("bank-transfer", "Bank transfer", "courier", "Courier", 4.90m, 21m);
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
        order.AddLine(Guid.NewGuid(), "Oak Chair", 121m, 21m, 2);

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

    private static readonly ShipmentDetails Parcel = new("Courier", "PKG-1", null);

    private static Order Place() => new(
        Guid.NewGuid(),
        "2026-00001",
        "EUR",
        "buyer@example.test",
        Address,
        Address,
        Methods,
        pickupPoint: null,
        PlacedAt,
        PlacedAt.AddMinutes(30));
}

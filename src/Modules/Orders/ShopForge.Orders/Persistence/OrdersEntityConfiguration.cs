using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Orders.Domain;

namespace ShopForge.Orders.Persistence;

internal sealed class CartEntityConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts", OrdersModule.Schema);

        builder.Property(cart => cart.DiscountCode).HasMaxLength(40);

        builder.OwnsMany(cart => cart.Lines, lines =>
        {
            lines.ToTable("cart_lines", OrdersModule.Schema, table =>
                table.HasCheckConstraint("ck_cart_lines_quantity", $"quantity > 0 AND quantity <= {Cart.MaxQuantity}"));
            lines.WithOwner().HasForeignKey("CartId");
            lines.HasKey("CartId", nameof(CartLine.StoreProductId), nameof(CartLine.VariantId));
        });
    }
}

internal sealed class OrderEntityConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", OrdersModule.Schema, table =>
            table.HasCheckConstraint("ck_orders_shipping_price", "shipping_price >= 0"));

        // Two writers can reach for one order at the same moment — the store marking it paid while the provider's
        // webhook says the same thing, a cancel racing the expiry sweep. PostgreSQL stamps every row version with
        // xmin, so the update carries the version it read and the second writer changes nothing and is told (D-130).
        builder.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();

        builder.Property(order => order.Number).HasMaxLength(20);
        builder.Property(order => order.Email).HasMaxLength(254);
        builder.Property(order => order.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(order => order.PaymentMethodCode).HasMaxLength(50);
        builder.Property(order => order.PaymentMethodName).HasMaxLength(100);
        builder.Property(order => order.PaymentProviderKey).HasMaxLength(50);
        builder.Property(order => order.ShippingMethodCode).HasMaxLength(50);
        builder.Property(order => order.ShippingMethodName).HasMaxLength(100);
        builder.Property(order => order.ShippingPrice).HasPrecision(12, 2);
        builder.Property(order => order.ShippingDiscount).HasPrecision(12, 2);
        builder.Property(order => order.DiscountTotal).HasPrecision(12, 2);
        builder.Property(order => order.RefundedTotal).HasPrecision(12, 2);
        builder.Property(order => order.DiscountCode).HasMaxLength(40);
        builder.Property(order => order.DiscountName).HasMaxLength(100);
        builder.Property(order => order.ShippingVatRate).HasPrecision(5, 2);
        builder.Ignore(order => order.ItemsTotal);
        builder.Ignore(order => order.ShippingCharged);
        builder.Ignore(order => order.GrandTotal);
        builder.Ignore(order => order.VatTotal);

        builder.Property(order => order.PaymentReference).HasMaxLength(100);
        builder.Property(order => order.PickupPointCode).HasMaxLength(50);
        builder.Property(order => order.PickupPointName).HasMaxLength(100);

        builder.ComplexProperty(order => order.BillingAddress, address => ConfigureAddress(address, "billing"));
        builder.ComplexProperty(order => order.ShippingAddress, address => ConfigureAddress(address, "shipping"));
        builder.OwnsOne(order => order.PickupPointAddress, address => ConfigureOwnedAddress(address, "pickup_point"));

        builder.OwnsOne(order => order.Shipment, shipment =>
        {
            shipment.Property(value => value.Carrier).HasMaxLength(100).HasColumnName("shipment_carrier");
            shipment.Property(value => value.TrackingNumber).HasMaxLength(100).HasColumnName("shipment_tracking_number");
            shipment.Property(value => value.TrackingUrl).HasMaxLength(500).HasColumnName("shipment_tracking_url");
            shipment.Property(value => value.ShippedAt).HasColumnName("shipment_shipped_at");
        });

        builder.HasIndex(order => new { order.StoreId, order.Number }).IsUnique();
        builder.HasIndex(order => new { order.StoreId, order.PlacedAt });
        builder.HasIndex(order => new { order.StoreId, order.StoreCustomerId });
        builder.HasIndex(order => new { order.StoreId, order.Email });

        builder.OwnsMany(order => order.Lines, lines =>
        {
            lines.ToTable("order_lines", OrdersModule.Schema);
            lines.WithOwner().HasForeignKey("OrderId");
            lines.Property(line => line.ProductName).HasMaxLength(200);
            lines.Property(line => line.UnitPrice).HasPrecision(12, 2);
            lines.Property(line => line.Discount).HasPrecision(12, 2);
            lines.Property(line => line.VatRate).HasPrecision(5, 2);
            lines.Ignore(line => line.LineTotal);
            lines.Ignore(line => line.VatAmount);
        });
    }

    private static void ConfigureOwnedAddress(OwnedNavigationBuilder<Order, Address> address, string prefix)
    {
        address.Property(value => value.FullName).HasMaxLength(200).HasColumnName($"{prefix}_full_name");
        address.Property(value => value.Line1).HasMaxLength(200).HasColumnName($"{prefix}_line1");
        address.Property(value => value.Line2).HasMaxLength(200).HasColumnName($"{prefix}_line2");
        address.Property(value => value.City).HasMaxLength(100).HasColumnName($"{prefix}_city");
        address.Property(value => value.PostalCode).HasMaxLength(20).HasColumnName($"{prefix}_postal_code");
        address.Property(value => value.Country).HasMaxLength(2).IsFixedLength().HasColumnName($"{prefix}_country");
    }

    private static void ConfigureAddress(ComplexPropertyBuilder<Address> address, string prefix)
    {
        address.Property(value => value.FullName).HasMaxLength(200).HasColumnName($"{prefix}_full_name");
        address.Property(value => value.Line1).HasMaxLength(200).HasColumnName($"{prefix}_line1");
        address.Property(value => value.Line2).HasMaxLength(200).HasColumnName($"{prefix}_line2");
        address.Property(value => value.City).HasMaxLength(100).HasColumnName($"{prefix}_city");
        address.Property(value => value.PostalCode).HasMaxLength(20).HasColumnName($"{prefix}_postal_code");
        address.Property(value => value.Country).HasMaxLength(2).IsFixedLength().HasColumnName($"{prefix}_country");
    }
}

internal sealed class PaymentMethodEntityConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("payment_methods", OrdersModule.Schema);

        builder.Property(method => method.Code).HasMaxLength(50);
        builder.Property(method => method.Name).HasMaxLength(100);
        builder.Property(method => method.ProviderKey).HasMaxLength(50);

        builder.HasIndex(method => new { method.StoreId, method.Code }).IsUnique();
    }
}

internal sealed class ShippingMethodEntityConfiguration : IEntityTypeConfiguration<ShippingMethod>
{
    public void Configure(EntityTypeBuilder<ShippingMethod> builder)
    {
        builder.ToTable("shipping_methods", OrdersModule.Schema, table =>
            table.HasCheckConstraint("ck_shipping_methods_price", "price >= 0 AND vat_rate >= 0 AND vat_rate <= 100"));

        builder.Property(method => method.Code).HasMaxLength(50);
        builder.Property(method => method.Name).HasMaxLength(100);
        builder.Property(method => method.ProviderKey).HasMaxLength(50);
        builder.Property(method => method.Price).HasPrecision(12, 2);
        builder.Property(method => method.VatRate).HasPrecision(5, 2);

        builder.HasIndex(method => new { method.StoreId, method.Code }).IsUnique();
    }
}

internal sealed class NumberSequenceEntityConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        builder.ToTable("number_sequences", OrdersModule.Schema);

        builder.Property(sequence => sequence.Series).HasMaxLength(20);
        builder.HasKey(sequence => new { sequence.StoreId, sequence.Series, sequence.Year });
    }
}

internal sealed class StorePickupPointEntityConfiguration : IEntityTypeConfiguration<StorePickupPoint>
{
    public void Configure(EntityTypeBuilder<StorePickupPoint> builder)
    {
        builder.ToTable("pickup_points", OrdersModule.Schema);

        builder.Property(point => point.Code).HasMaxLength(50);
        builder.Property(point => point.Name).HasMaxLength(100);
        builder.ComplexProperty(point => point.Address, address =>
        {
            address.Property(value => value.FullName).HasMaxLength(200);
            address.Property(value => value.Line1).HasMaxLength(200);
            address.Property(value => value.Line2).HasMaxLength(200);
            address.Property(value => value.City).HasMaxLength(100);
            address.Property(value => value.PostalCode).HasMaxLength(20);
            address.Property(value => value.Country).HasMaxLength(2).IsFixedLength();
        });

        builder.HasIndex(point => new { point.StoreId, point.Code }).IsUnique();
    }
}

internal sealed class PaymentEventEntityConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable("payment_events", OrdersModule.Schema);

        builder.Property(paymentEvent => paymentEvent.Provider).HasMaxLength(50);
        builder.Property(paymentEvent => paymentEvent.EventId).HasMaxLength(100);
        builder.Property(paymentEvent => paymentEvent.OrderNumber).HasMaxLength(20);

        builder.HasIndex(paymentEvent => new { paymentEvent.StoreId, paymentEvent.Provider, paymentEvent.EventId }).IsUnique();
    }
}

internal sealed class InvoiceEntityConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", OrdersModule.Schema);

        builder.Property(invoice => invoice.Number).HasMaxLength(30);
        builder.Property(invoice => invoice.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(invoice => invoice.OrderNumber).HasMaxLength(20);
        builder.Property(invoice => invoice.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(invoice => invoice.BuyerEmail).HasMaxLength(254);
        builder.Property(invoice => invoice.PaymentMethodName).HasMaxLength(100);
        builder.Ignore(invoice => invoice.Total);
        builder.Ignore(invoice => invoice.VatTotal);
        builder.Ignore(invoice => invoice.NetTotal);

        builder.ComplexProperty(invoice => invoice.Seller, seller =>
        {
            seller.Property(value => value.LegalName).HasMaxLength(200).HasColumnName("seller_legal_name");
            seller.Property(value => value.Line1).HasMaxLength(200).HasColumnName("seller_line1");
            seller.Property(value => value.City).HasMaxLength(100).HasColumnName("seller_city");
            seller.Property(value => value.PostalCode).HasMaxLength(20).HasColumnName("seller_postal_code");
            seller.Property(value => value.Country).HasMaxLength(2).IsFixedLength().HasColumnName("seller_country");
            seller.Property(value => value.RegistrationNumber).HasMaxLength(50).HasColumnName("seller_registration_number");
            seller.Property(value => value.VatNumber).HasMaxLength(50).HasColumnName("seller_vat_number");
        });

        builder.ComplexProperty(invoice => invoice.Buyer, buyer =>
        {
            buyer.Property(value => value.FullName).HasMaxLength(200).HasColumnName("buyer_full_name");
            buyer.Property(value => value.Line1).HasMaxLength(200).HasColumnName("buyer_line1");
            buyer.Property(value => value.Line2).HasMaxLength(200).HasColumnName("buyer_line2");
            buyer.Property(value => value.City).HasMaxLength(100).HasColumnName("buyer_city");
            buyer.Property(value => value.PostalCode).HasMaxLength(20).HasColumnName("buyer_postal_code");
            buyer.Property(value => value.Country).HasMaxLength(2).IsFixedLength().HasColumnName("buyer_country");
        });

        builder.OwnsMany(invoice => invoice.Lines, lines =>
        {
            lines.ToTable("invoice_lines", OrdersModule.Schema);
            lines.WithOwner().HasForeignKey("InvoiceId");
            lines.Property(line => line.Description).HasMaxLength(200);
            lines.Property(line => line.UnitPrice).HasPrecision(12, 2);
            lines.Property(line => line.Discount).HasPrecision(12, 2);
            lines.Property(line => line.VatRate).HasPrecision(5, 2);
            lines.Ignore(line => line.LineTotal);
        });

        builder.HasIndex(invoice => new { invoice.StoreId, invoice.Number }).IsUnique();
        builder.HasIndex(invoice => new { invoice.StoreId, invoice.OrderNumber });

        // One credit note per return, whatever a retry or a second pair of hands tries (D-097).
        builder.HasIndex(invoice => invoice.ReturnId).IsUnique().HasFilter("return_id IS NOT NULL");

        // And one invoice per order: the code looks for an existing one first, but two deliveries of the payment
        // event at the same moment would both look and both find nothing.
        builder.HasIndex(invoice => new { invoice.StoreId, invoice.OrderNumber, invoice.Kind })
            .IsUnique()
            .HasFilter("return_id IS NULL")
            .HasDatabaseName("ix_invoices_store_id_order_number_kind");
    }
}

internal sealed class OrderReturnEntityConfiguration : IEntityTypeConfiguration<OrderReturn>
{
    public void Configure(EntityTypeBuilder<OrderReturn> builder)
    {
        builder.ToTable("order_returns", OrdersModule.Schema);

        builder.Property(orderReturn => orderReturn.Number).HasMaxLength(30);
        builder.Property(orderReturn => orderReturn.OrderNumber).HasMaxLength(20);
        builder.Property(orderReturn => orderReturn.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(orderReturn => orderReturn.Reason).HasMaxLength(OrderReturn.MaxReasonLength);
        builder.Property(orderReturn => orderReturn.RefundedAmount).HasPrecision(12, 2);
        builder.Ignore(orderReturn => orderReturn.HoldsGoods);

        builder.OwnsMany(orderReturn => orderReturn.Lines, lines =>
        {
            lines.ToTable("order_return_lines", OrdersModule.Schema, table =>
                table.HasCheckConstraint("ck_order_return_lines_quantity", "quantity > 0"));
            lines.WithOwner().HasForeignKey("OrderReturnId");
            lines.HasKey("OrderReturnId", nameof(OrderReturnLine.StoreProductId), nameof(OrderReturnLine.VariantId));
            lines.Property(line => line.ProductName).HasMaxLength(200);
        });

        builder.HasIndex(orderReturn => new { orderReturn.StoreId, orderReturn.Number }).IsUnique();
        builder.HasIndex(orderReturn => new { orderReturn.StoreId, orderReturn.OrderNumber });
        builder.HasIndex(orderReturn => new { orderReturn.StoreId, orderReturn.Status });
    }
}

internal sealed class DiscountEntityConfiguration : IEntityTypeConfiguration<Discount>
{
    public void Configure(EntityTypeBuilder<Discount> builder)
    {
        builder.ToTable("discounts", OrdersModule.Schema, table =>
            table.HasCheckConstraint("ck_discounts_value", "value >= 0 AND redemptions >= 0"));

        builder.Property(discount => discount.Code).HasMaxLength(40);
        builder.Property(discount => discount.Name).HasMaxLength(100);
        builder.Property(discount => discount.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(discount => discount.Value).HasPrecision(12, 2);
        builder.Property(discount => discount.MinimumOrderAmount).HasPrecision(12, 2);

        builder.HasIndex(discount => new { discount.StoreId, discount.Code }).IsUnique();
    }
}

internal sealed class DiscountRedemptionEntityConfiguration : IEntityTypeConfiguration<DiscountRedemption>
{
    public void Configure(EntityTypeBuilder<DiscountRedemption> builder)
    {
        builder.ToTable("discount_redemptions", OrdersModule.Schema);

        builder.Property(redemption => redemption.OrderNumber).HasMaxLength(20);
        builder.Property(redemption => redemption.Email).HasMaxLength(254);
        builder.Property(redemption => redemption.Amount).HasPrecision(12, 2);

        builder.HasOne<Discount>().WithMany().HasForeignKey(redemption => redemption.DiscountId);
        builder.HasIndex(redemption => new { redemption.StoreId, redemption.DiscountId, redemption.Email });
    }
}

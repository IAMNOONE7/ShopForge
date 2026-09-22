using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Orders.Domain;

namespace ShopForge.Orders.Persistence;

internal sealed class CartEntityConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("carts", OrdersModule.Schema);

        builder.OwnsMany(cart => cart.Lines, lines =>
        {
            lines.ToTable("cart_lines", OrdersModule.Schema, table =>
                table.HasCheckConstraint("ck_cart_lines_quantity", $"quantity > 0 AND quantity <= {Cart.MaxQuantity}"));
            lines.WithOwner().HasForeignKey("CartId");
            lines.HasKey("CartId", nameof(CartLine.StoreProductId));
        });
    }
}

internal sealed class OrderEntityConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", OrdersModule.Schema, table =>
            table.HasCheckConstraint("ck_orders_shipping_price", "shipping_price >= 0"));

        builder.Property(order => order.Number).HasMaxLength(20);
        builder.Property(order => order.Email).HasMaxLength(254);
        builder.Property(order => order.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(order => order.PaymentMethodCode).HasMaxLength(50);
        builder.Property(order => order.PaymentMethodName).HasMaxLength(100);
        builder.Property(order => order.ShippingMethodCode).HasMaxLength(50);
        builder.Property(order => order.ShippingMethodName).HasMaxLength(100);
        builder.Property(order => order.ShippingPrice).HasPrecision(12, 2);
        builder.Property(order => order.ShippingVatRate).HasPrecision(5, 2);
        builder.Ignore(order => order.ItemsTotal);
        builder.Ignore(order => order.GrandTotal);
        builder.Ignore(order => order.VatTotal);

        builder.ComplexProperty(order => order.BillingAddress, address => ConfigureAddress(address, "billing"));
        builder.ComplexProperty(order => order.ShippingAddress, address => ConfigureAddress(address, "shipping"));

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
            lines.Property(line => line.VatRate).HasPrecision(5, 2);
            lines.Ignore(line => line.LineTotal);
            lines.Ignore(line => line.VatAmount);
        });
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

internal sealed class OrderNumberSequenceEntityConfiguration : IEntityTypeConfiguration<OrderNumberSequence>
{
    public void Configure(EntityTypeBuilder<OrderNumberSequence> builder)
    {
        builder.ToTable("order_numbers", OrdersModule.Schema);

        builder.HasKey(sequence => new { sequence.StoreId, sequence.Year });
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

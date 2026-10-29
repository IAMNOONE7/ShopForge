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
        builder.Property(order => order.Phone).HasMaxLength(30);
        builder.Property(order => order.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(order => order.PaymentMethodCode).HasMaxLength(50);
        builder.Property(order => order.PaymentMethodName).HasMaxLength(100);
        builder.Property(order => order.PaymentProviderKey).HasMaxLength(50);
        builder.Property(order => order.ShippingProviderKey).HasMaxLength(50);
        builder.Property(order => order.ShippingMethodCode).HasMaxLength(50);
        builder.Property(order => order.ShippingMethodName).HasMaxLength(100);
        builder.Property(order => order.ShippingPrice).HasPrecision(12, 2);
        builder.Property(order => order.ShippingDiscount).HasPrecision(12, 2);
        builder.Property(order => order.FreeShippingThreshold).HasPrecision(12, 2);
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
            lines.Property(line => line.ProductName).HasMaxLength(OrderLine.MaxProductNameLength);
            lines.Property(line => line.UnitPrice).HasPrecision(12, 2);
            lines.Property(line => line.Discount).HasPrecision(12, 2);
            lines.Property(line => line.VatRate).HasPrecision(5, 2);
            lines.Ignore(line => line.LineTotal);
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
        {
            table.HasCheckConstraint("ck_shipping_methods_price", "price >= 0 AND vat_rate >= 0 AND vat_rate <= 100");

            // A method priced by the parcel sends by a service, and only it limits its parcels: a weight of
            // something, and all three sides or none (D-218).
            table.HasCheckConstraint(
                "ck_shipping_methods_parcels",
                "(pricing = 'PerParcel' AND carrier_service_key IS NOT NULL) "
                + "OR (pricing = 'Flat' AND parcel_max_weight_grams IS NULL AND parcel_longest_mm IS NULL)");
            table.HasCheckConstraint(
                "ck_shipping_methods_parcel_limits",
                "(parcel_max_weight_grams IS NULL OR parcel_max_weight_grams > 0) AND "
                + "((parcel_longest_mm IS NULL AND parcel_middle_mm IS NULL AND parcel_shortest_mm IS NULL) "
                + "OR (parcel_longest_mm >= parcel_middle_mm AND parcel_middle_mm >= parcel_shortest_mm AND parcel_shortest_mm > 0))");
        });

        builder.Property(method => method.Code).HasMaxLength(50);
        builder.Property(method => method.Name).HasMaxLength(100);
        builder.Property(method => method.ProviderKey).HasMaxLength(50);
        builder.Property(method => method.Price).HasPrecision(12, 2);
        builder.Property(method => method.VatRate).HasPrecision(5, 2);
        builder.Property(method => method.TaxClass).HasMaxLength(50);
        builder.PrimitiveCollection(method => method.Countries).ElementType(country => country.HasMaxLength(2).IsFixedLength());
        builder.Property(method => method.Pricing).HasConversion<string>().HasMaxLength(20).HasDefaultValue(ShippingPricing.Flat).HasSentinel(ShippingPricing.Flat);
        builder.Property(method => method.CarrierServiceKey).HasMaxLength(CarrierService.MaxKeyLength);
        builder.Property(method => method.RatesFrom).HasConversion<string>().HasMaxLength(20);

        builder.OwnsMany(method => method.RateBands, bands =>
        {
            bands.ToTable("shipping_rate_bands", OrdersModule.Schema, table =>
                table.HasCheckConstraint("ck_shipping_rate_bands_values", "up_to_grams > 0 AND price >= 0"));
            bands.WithOwner().HasForeignKey("ShippingMethodId");
            bands.Property(band => band.Price).HasPrecision(12, 2);
        });

        builder.HasIndex(method => new { method.StoreId, method.Code }).IsUnique();
    }
}

internal sealed class CarrierServiceEntityConfiguration : IEntityTypeConfiguration<CarrierService>
{
    public void Configure(EntityTypeBuilder<CarrierService> builder)
    {
        builder.ToTable("carrier_services", OrdersModule.Schema, table =>
            table.HasCheckConstraint(
                "ck_carrier_services_limits",
                "max_weight_grams > 0 AND largest_volume_mm3 > 0 AND (max_parcels IS NULL OR max_parcels >= 1) "
                + "AND longest_mm >= middle_mm AND middle_mm >= shortest_mm "
                + "AND min_longest_mm >= min_middle_mm AND min_middle_mm >= min_shortest_mm AND min_shortest_mm > 0 "
                + "AND min_longest_mm <= longest_mm AND min_middle_mm <= middle_mm AND min_shortest_mm <= shortest_mm"));

        builder.Property(service => service.Key).HasMaxLength(CarrierService.MaxKeyLength);
        builder.Property(service => service.ProviderKey).HasMaxLength(50);
        builder.Property(service => service.Name).HasMaxLength(100);
        builder.Property(service => service.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(service => service.Country).HasMaxLength(2).IsFixedLength();
        builder.Property(service => service.Source).HasMaxLength(CarrierService.MaxSourceLength);

        builder.HasIndex(service => service.Key).IsUnique();
    }
}

internal sealed class ShippingSettingsEntityConfiguration : IEntityTypeConfiguration<ShippingSettings>
{
    public void Configure(EntityTypeBuilder<ShippingSettings> builder)
    {
        builder.ToTable("shipping_settings", OrdersModule.Schema, table =>
            table.HasCheckConstraint(
                "ck_shipping_settings_values",
                "packaging_weight_grams >= 0 AND fill_percent BETWEEN 1 AND 100 AND largest_parcel_order >= 1"));

        builder.HasKey(settings => settings.StoreId);
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

internal sealed class PaymentAttemptEntityConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("payment_attempts", OrdersModule.Schema);

        builder.Property(attempt => attempt.OrderNumber).HasMaxLength(20);
        builder.Property(attempt => attempt.Provider).HasMaxLength(50);
        builder.Property(attempt => attempt.Reference).HasMaxLength(200);
        builder.Property(attempt => attempt.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(attempt => attempt.Amount).HasPrecision(12, 2);
        builder.Property(attempt => attempt.RedirectUrl).HasMaxLength(2000);
        builder.Property(attempt => attempt.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(attempt => attempt.Environment).HasMaxLength(10);
        builder.Ignore(attempt => attempt.IsFinished);

        builder.HasIndex(attempt => new { attempt.StoreId, attempt.OrderNumber });

        // What a provider calls an attempt is its own, so it names one attempt and no other. A method the store
        // settles itself has no reference at all, and PostgreSQL lets as many nulls through a unique index as
        // there are such methods.
        builder.HasIndex(attempt => new { attempt.StoreId, attempt.Provider, attempt.Reference }).IsUnique();
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

internal sealed class UnappliedPaymentEntityConfiguration : IEntityTypeConfiguration<UnappliedPayment>
{
    public void Configure(EntityTypeBuilder<UnappliedPayment> builder)
    {
        builder.ToTable("unapplied_payments", OrdersModule.Schema);

        builder.Property(payment => payment.OrderNumber).HasMaxLength(20);
        builder.Property(payment => payment.Provider).HasMaxLength(50);
        builder.Property(payment => payment.Reference).HasMaxLength(200);
        builder.Property(payment => payment.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(payment => payment.Amount).HasPrecision(12, 2);
        builder.Property(payment => payment.Reason).HasConversion<string>().HasMaxLength(20);
        builder.Property(payment => payment.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(payment => payment.Note).HasMaxLength(UnappliedPayment.MaxNoteLength);

        // Closing by hand writes through the change tracker while a refund moves the row with a conditional update,
        // so the version it read goes with it: a refund that got there first is not quietly overwritten (D-130).
        builder.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();

        builder.HasIndex(payment => new { payment.StoreId, payment.OrderNumber });
        builder.HasIndex(payment => new { payment.StoreId, payment.Status });

        // One record per payment however often it is reported, so it can only ever be refunded once. A provider
        // that named no payment leaves a null, and those are as many as there are.
        builder.HasIndex(payment => new { payment.StoreId, payment.Provider, payment.Reference }).IsUnique();
    }
}

internal sealed class ProviderStatementLineEntityConfiguration : IEntityTypeConfiguration<ProviderStatementLine>
{
    public void Configure(EntityTypeBuilder<ProviderStatementLine> builder)
    {
        builder.ToTable("provider_statement_lines", OrdersModule.Schema);

        builder.Property(line => line.Provider).HasMaxLength(50);
        builder.Property(line => line.TransferId).HasMaxLength(50);
        builder.Property(line => line.TransactionId).HasMaxLength(200);
        builder.Property(line => line.OrderNumber).HasMaxLength(200);
        builder.Property(line => line.Kind).HasMaxLength(50);
        builder.Property(line => line.Currency).HasMaxLength(3);
        builder.Property(line => line.Confirmed).HasPrecision(12, 2);
        builder.Property(line => line.Fee).HasPrecision(12, 2);
        builder.Property(line => line.Transferred).HasPrecision(12, 2);
        builder.Property(line => line.Problem).HasMaxLength(ProviderStatementLine.MaxProblemLength);
        builder.Ignore(line => line.IsPayment);

        // A payment is found by its transaction whatever day it was paid out on, and a day is laid out by when
        // the shopper paid.
        builder.HasIndex(line => new { line.StoreId, line.Provider, line.TransactionId });
        builder.HasIndex(line => new { line.StoreId, line.Provider, line.PaidOn });
    }
}

internal sealed class ProviderStatementDayEntityConfiguration : IEntityTypeConfiguration<ProviderStatementDay>
{
    public void Configure(EntityTypeBuilder<ProviderStatementDay> builder)
    {
        builder.ToTable("provider_statement_days", OrdersModule.Schema);

        builder.Property(day => day.Provider).HasMaxLength(50);

        // A day is read once: two passes reaching for the same day race for this, and the loser reads it again
        // as already done.
        builder.HasIndex(day => new { day.StoreId, day.Provider, day.Day }).IsUnique();
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

// A table rather than a document, because booking keeps the parcels actually sent beside these and gives each a
// tracking number (D-214, V8).
internal sealed class OrderParcelEntityConfiguration : IEntityTypeConfiguration<OrderParcel>
{
    public void Configure(EntityTypeBuilder<OrderParcel> builder)
    {
        builder.ToTable("order_parcels", OrdersModule.Schema, table =>
            table.HasCheckConstraint("ck_order_parcels_values", "number >= 1 AND gross_weight_grams > 0 AND band_up_to_grams >= gross_weight_grams AND price >= 0"));

        builder.Property(parcel => parcel.OrderNumber).HasMaxLength(20);
        builder.Property(parcel => parcel.Price).HasPrecision(12, 2);

        builder.OwnsMany(parcel => parcel.Contents, contents =>
        {
            contents.ToTable("order_parcel_items", OrdersModule.Schema, table =>
                table.HasCheckConstraint("ck_order_parcel_items_quantity", "quantity >= 1"));
            contents.Property(item => item.Sku).HasMaxLength(64);
        });

        builder.HasIndex(parcel => new { parcel.StoreId, parcel.OrderNumber, parcel.Number }).IsUnique();
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
        builder.Property(orderReturn => orderReturn.Refund).HasColumnName("refund_status").HasConversion<string>().HasMaxLength(20);
        builder.Property(orderReturn => orderReturn.RefundProblem).HasMaxLength(OrderReturn.MaxRefundTextLength);
        builder.Property(orderReturn => orderReturn.RefundNote).HasMaxLength(OrderReturn.MaxRefundTextLength);
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
        builder.HasIndex(orderReturn => new { orderReturn.StoreId, orderReturn.Refund });
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

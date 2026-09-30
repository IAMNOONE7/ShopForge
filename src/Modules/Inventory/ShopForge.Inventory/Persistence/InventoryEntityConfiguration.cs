using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Inventory.Domain;

namespace ShopForge.Inventory.Persistence;

internal sealed class WarehouseEntityConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouses", InventoryModule.Schema);

        builder.Property(warehouse => warehouse.Code).HasMaxLength(50);
        builder.Property(warehouse => warehouse.Name).HasMaxLength(100);

        builder.HasIndex(warehouse => new { warehouse.TenantId, warehouse.Code }).IsUnique();
    }
}

internal sealed class InventoryItemEntityConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items", InventoryModule.Schema, table =>
            table.HasCheckConstraint("ck_inventory_items_quantities", "quantity_on_hand >= 0 AND quantity_reserved >= 0 AND quantity_reserved <= quantity_on_hand"));

        builder.Ignore(item => item.QuantityAvailable);

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(item => item.WarehouseId);
        builder.HasIndex(item => new { item.WarehouseId, item.VariantId }).IsUnique();

        // Stock moved from the product to the variant. The old column stays, unread, for the release after this
        // one to drop: a change that takes data with it is two releases (D-125).
        builder.Property<Guid?>("ProductId");
    }
}

internal sealed class StockMovementEntityConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", InventoryModule.Schema);

        builder.Property(movement => movement.Reason).HasConversion<string>().HasMaxLength(20);
        builder.Property(movement => movement.Reference).HasMaxLength(50);

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(movement => movement.WarehouseId);
        builder.HasIndex(movement => new { movement.TenantId, movement.VariantId, movement.OccurredAt });
        builder.Property<Guid?>("ProductId");
    }
}

internal sealed class StockReservationEntityConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations", InventoryModule.Schema, table =>
            table.HasCheckConstraint("ck_stock_reservations_quantity", "quantity > 0"));

        builder.Property(reservation => reservation.Reference).HasMaxLength(50);
        builder.Property(reservation => reservation.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(reservation => reservation.WarehouseId);
        builder.HasIndex(reservation => new { reservation.TenantId, reservation.Reference });
        builder.Property<Guid?>("ProductId");
    }
}

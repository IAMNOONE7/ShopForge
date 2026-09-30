using ShopForge.Shared.Tenancy;

namespace ShopForge.Inventory.Domain;

internal sealed class StockMovement : ITenantOwned
{
    private StockMovement()
    {
    }

    public StockMovement(Guid tenantId, Guid warehouseId, Guid variantId, int quantity, StockMovementReason reason, string reference, DateTimeOffset occurredAt)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        WarehouseId = warehouseId;
        VariantId = variantId;
        Quantity = quantity;
        Reason = reason;
        Reference = reference;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid VariantId { get; private set; }

    // Signed: stock coming in is positive, stock leaving is negative.
    public int Quantity { get; private set; }

    public StockMovementReason Reason { get; private set; }

    public string Reference { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }
}

internal enum StockMovementReason
{
    Adjustment,
    Sale,
    Return,
}

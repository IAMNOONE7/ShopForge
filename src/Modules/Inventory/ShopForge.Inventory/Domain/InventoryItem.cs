using ShopForge.Shared.Tenancy;

namespace ShopForge.Inventory.Domain;

// What is on the shelf, counted per variant: a shop with three sizes of a shirt has three of these (D-135).
internal sealed class InventoryItem : ITenantOwned
{
    private InventoryItem()
    {
    }

    public InventoryItem(Guid tenantId, Guid warehouseId, Guid variantId)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        WarehouseId = warehouseId;
        VariantId = variantId;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid VariantId { get; private set; }

    public int QuantityOnHand { get; private set; }

    public int QuantityReserved { get; private set; }

    public int QuantityAvailable => QuantityOnHand - QuantityReserved;
}

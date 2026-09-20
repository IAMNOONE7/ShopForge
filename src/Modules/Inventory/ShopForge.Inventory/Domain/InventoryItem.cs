using ShopForge.Shared.Tenancy;

namespace ShopForge.Inventory.Domain;

internal sealed class InventoryItem : ITenantOwned
{
    private InventoryItem()
    {
    }

    public InventoryItem(Guid tenantId, Guid warehouseId, Guid productId)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        WarehouseId = warehouseId;
        ProductId = productId;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid ProductId { get; private set; }

    public int QuantityOnHand { get; private set; }

    public int QuantityReserved { get; private set; }

    public int QuantityAvailable => QuantityOnHand - QuantityReserved;
}

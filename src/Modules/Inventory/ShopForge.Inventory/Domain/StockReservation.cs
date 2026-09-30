using ShopForge.Shared.Tenancy;

namespace ShopForge.Inventory.Domain;

internal sealed class StockReservation : ITenantOwned
{
    private StockReservation()
    {
    }

    public StockReservation(Guid tenantId, Guid warehouseId, Guid variantId, int quantity, string reference, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        WarehouseId = warehouseId;
        VariantId = variantId;
        Quantity = quantity;
        Reference = reference;
        ExpiresAt = expiresAt;
        Status = ReservationStatus.Held;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid VariantId { get; private set; }

    public int Quantity { get; private set; }

    // The order number the reservation was taken for; reservations are confirmed or released by it.
    public string Reference { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    // Changed by the ledger with a conditional update rather than here, so only one run can act on it (D-047).
    public ReservationStatus Status { get; private set; }
}

internal enum ReservationStatus
{
    Held,
    Confirmed,
    Released,
}

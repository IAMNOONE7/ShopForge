using ShopForge.Shared.Tenancy;

namespace ShopForge.Inventory.Domain;

internal sealed class StockReservation : ITenantOwned
{
    private StockReservation()
    {
    }

    public StockReservation(Guid tenantId, Guid warehouseId, Guid productId, int quantity, string reference, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        WarehouseId = warehouseId;
        ProductId = productId;
        Quantity = quantity;
        Reference = reference;
        ExpiresAt = expiresAt;
        Status = ReservationStatus.Held;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public Guid ProductId { get; private set; }

    public int Quantity { get; private set; }

    // The order number the reservation was taken for; reservations are confirmed or released by it.
    public string Reference { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public ReservationStatus Status { get; private set; }

    public void Confirm() => Status = ReservationStatus.Confirmed;

    public void Release() => Status = ReservationStatus.Released;
}

internal enum ReservationStatus
{
    Held,
    Confirmed,
    Released,
}

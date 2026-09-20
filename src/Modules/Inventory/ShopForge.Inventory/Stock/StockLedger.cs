using Microsoft.EntityFrameworkCore;
using ShopForge.Inventory.Domain;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Inventory.Stock;

// Reserving and issuing stock runs as conditional updates (D-047): the database rejects a change that would oversell,
// so concurrent checkouts cannot both take the last item. Callers that also write their own rows (checkout, admin)
// wrap these calls in a transaction.
internal sealed class StockLedger(DbContext dbContext, IStoreContext storeContext, TimeProvider clock) : IStockLedger
{
    public async Task<IReadOnlyDictionary<Guid, int>> AvailableAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
        await dbContext.Set<InventoryItem>()
            .Where(item => productIds.Contains(item.ProductId))
            .GroupBy(item => item.ProductId)
            .Select(group => new { ProductId = group.Key, Available = group.Sum(item => item.QuantityOnHand - item.QuantityReserved) })
            .ToDictionaryAsync(row => row.ProductId, row => row.Available, cancellationToken);

    public async Task<StockReservationResult> ReserveAsync(
        IReadOnlyCollection<StockRequest> requests,
        string reference,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var warehouseId = await DefaultWarehouseIdAsync(cancellationToken);
        var unavailable = new List<Guid>();

        foreach (var request in requests)
        {
            var reserved = await dbContext.Set<InventoryItem>()
                .Where(item => item.WarehouseId == warehouseId
                    && item.ProductId == request.ProductId
                    && item.QuantityOnHand - item.QuantityReserved >= request.Quantity)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.QuantityReserved, item => item.QuantityReserved + request.Quantity),
                    cancellationToken);

            if (reserved == 0)
            {
                unavailable.Add(request.ProductId);
                continue;
            }

            dbContext.Add(new StockReservation(TenantId, warehouseId, request.ProductId, request.Quantity, reference, expiresAt));
        }

        if (unavailable.Count > 0)
        {
            return new StockReservationResult(unavailable);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return StockReservationResult.Reserved;
    }

    public async Task ConfirmAsync(string reference, CancellationToken cancellationToken)
    {
        foreach (var reservation in await HeldAsync(reference, cancellationToken))
        {
            await dbContext.Set<InventoryItem>()
                .Where(item => item.WarehouseId == reservation.WarehouseId && item.ProductId == reservation.ProductId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.QuantityOnHand, item => item.QuantityOnHand - reservation.Quantity)
                        .SetProperty(item => item.QuantityReserved, item => item.QuantityReserved - reservation.Quantity),
                    cancellationToken);

            dbContext.Add(new StockMovement(
                TenantId,
                reservation.WarehouseId,
                reservation.ProductId,
                -reservation.Quantity,
                StockMovementReason.Sale,
                reference,
                clock.GetUtcNow()));

            reservation.Confirm();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(string reference, CancellationToken cancellationToken)
    {
        foreach (var reservation in await HeldAsync(reference, cancellationToken))
        {
            await dbContext.Set<InventoryItem>()
                .Where(item => item.WarehouseId == reservation.WarehouseId && item.ProductId == reservation.ProductId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.QuantityReserved, item => item.QuantityReserved - reservation.Quantity),
                    cancellationToken);

            reservation.Release();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SetOnHandAsync(Guid productId, int quantity, string reference, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);

        var item = await ItemAsync(productId, cancellationToken);
        var previous = item.QuantityOnHand;

        // Stock already promised to an order cannot be adjusted away; the reservation has to be released first.
        var adjusted = await dbContext.Set<InventoryItem>()
            .Where(candidate => candidate.Id == item.Id && candidate.QuantityReserved <= quantity)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.QuantityOnHand, quantity), cancellationToken);

        if (adjusted == 0)
        {
            return false;
        }

        if (previous != quantity)
        {
            dbContext.Add(new StockMovement(
                TenantId,
                item.WarehouseId,
                productId,
                quantity - previous,
                StockMovementReason.Adjustment,
                reference,
                clock.GetUtcNow()));

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private Guid TenantId => storeContext.TenantId!.Value;

    private async Task<List<StockReservation>> HeldAsync(string reference, CancellationToken cancellationToken) =>
        await dbContext.Set<StockReservation>()
            .Where(reservation => reservation.Reference == reference && reservation.Status == ReservationStatus.Held)
            .ToListAsync(cancellationToken);

    private async Task<InventoryItem> ItemAsync(Guid productId, CancellationToken cancellationToken)
    {
        var warehouseId = await DefaultWarehouseIdAsync(cancellationToken);
        var item = await dbContext.Set<InventoryItem>()
            .SingleOrDefaultAsync(candidate => candidate.WarehouseId == warehouseId && candidate.ProductId == productId, cancellationToken);

        if (item is not null)
        {
            return item;
        }

        item = new InventoryItem(TenantId, warehouseId, productId);
        dbContext.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);

        return item;
    }

    private async Task<Guid> DefaultWarehouseIdAsync(CancellationToken cancellationToken)
    {
        var warehouse = await dbContext.Set<Warehouse>().SingleOrDefaultAsync(candidate => candidate.Code == Warehouse.DefaultCode, cancellationToken);

        if (warehouse is not null)
        {
            return warehouse.Id;
        }

        var entry = dbContext.Add(new Warehouse(TenantId, Warehouse.DefaultCode, "Main warehouse"));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            return entry.Entity.Id;
        }
        catch (DbUpdateException)
        {
            // Two requests created the tenant's first warehouse at once; the other one won.
            entry.State = EntityState.Detached;
        }

        return await dbContext.Set<Warehouse>()
            .Where(candidate => candidate.Code == Warehouse.DefaultCode)
            .Select(candidate => candidate.Id)
            .SingleAsync(cancellationToken);
    }
}

namespace ShopForge.Shared.Inventory;

// How other modules read and move stock without depending on the Inventory module. Stock belongs to a variant —
// the thing a warehouse actually counts — so every id here names one of those, never a product (D-135).
public interface IStockLedger
{
    Task<IReadOnlyDictionary<Guid, int>> AvailableAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken);

    Task<StockReservationResult> ReserveAsync(
        IReadOnlyCollection<StockRequest> requests,
        string reference,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task ConfirmAsync(string reference, CancellationToken cancellationToken);

    Task ReleaseAsync(string reference, CancellationToken cancellationToken);

    // Goods that came back: a refunded order puts its items on sale again (D-081).
    Task ReturnAsync(IReadOnlyCollection<StockRequest> requests, string reference, CancellationToken cancellationToken);

    // False when the quantity is below what orders already reserved; the reservations have to go first.
    Task<bool> SetOnHandAsync(Guid variantId, int quantity, string reference, CancellationToken cancellationToken);
}

public sealed record StockRequest(Guid VariantId, int Quantity);

public sealed record StockReservationResult(IReadOnlyList<Guid> UnavailableVariantIds)
{
    public static readonly StockReservationResult Reserved = new([]);

    public bool Succeeded => UnavailableVariantIds.Count == 0;
}

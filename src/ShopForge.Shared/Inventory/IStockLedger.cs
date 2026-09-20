namespace ShopForge.Shared.Inventory;

// How other modules read and move stock without depending on the Inventory module.
public interface IStockLedger
{
    Task<IReadOnlyDictionary<Guid, int>> AvailableAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    Task<StockReservationResult> ReserveAsync(
        IReadOnlyCollection<StockRequest> requests,
        string reference,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task ConfirmAsync(string reference, CancellationToken cancellationToken);

    Task ReleaseAsync(string reference, CancellationToken cancellationToken);

    // False when the quantity is below what orders already reserved; the reservations have to go first.
    Task<bool> SetOnHandAsync(Guid productId, int quantity, string reference, CancellationToken cancellationToken);
}

public sealed record StockRequest(Guid ProductId, int Quantity);

public sealed record StockReservationResult(IReadOnlyList<Guid> UnavailableProductIds)
{
    public static readonly StockReservationResult Reserved = new([]);

    public bool Succeeded => UnavailableProductIds.Count == 0;
}

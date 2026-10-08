namespace ShopForge.Shared.Catalog;

// Whether anything has ever been bought. The catalogue decides what may be deleted and orders know what has
// been sold, so the question crosses between them through here rather than one reading the other's tables
// (D-180).
public interface ISoldListings
{
    Task<bool> HasBeenOrderedAsync(Guid storeProductId, CancellationToken cancellationToken);
}

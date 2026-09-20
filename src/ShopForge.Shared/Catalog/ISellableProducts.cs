namespace ShopForge.Shared.Catalog;

// How other modules read the current store's catalog without depending on it.
public interface ISellableProducts
{
    Task<IReadOnlyList<SellableProduct>> FindAsync(IReadOnlyCollection<Guid> storeProductIds, CancellationToken cancellationToken);
}

public sealed record SellableProduct(Guid StoreProductId, Guid ProductId, string Name, string Slug, decimal Price, decimal VatRate, string? ImageUrl);

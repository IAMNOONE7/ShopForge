namespace ShopForge.Shared.Catalog;

// How other modules read the current store's catalog without depending on it.
public interface ISellableProducts
{
    Task<IReadOnlyList<SellableProduct>> FindAsync(IReadOnlyCollection<Guid> storeProductIds, CancellationToken cancellationToken);
}

public sealed record SellableProduct(
    Guid StoreProductId,
    Guid ProductId,
    string Name,
    string Slug,
    decimal Price,
    decimal VatRate,
    string? ImageUrl,
    IReadOnlyList<string> OptionNames,
    IReadOnlyList<SellableVariant> Variants)
{
    // A shopper who names no form of a thing sold in one form means that one.
    public SellableVariant? Only => Variants.Count == 1 ? Variants[0] : null;

    public SellableVariant? Form(Guid? variantId) =>
        variantId is null ? Only : Variants.SingleOrDefault(variant => variant.Id == variantId);
}

// Position is the order the merchant put the forms in — small, medium, large rather than large, medium,
// small — so anything listing them keeps that order instead of inventing an alphabetical one.
// The weight is what a carrier is told the parcel comes to, and null means the merchant has not said. A method
// with a weight limit cannot carry what it cannot weigh (D-145).
public sealed record SellableVariant(Guid Id, string Sku, IReadOnlyList<string> OptionValues, int Position, int? WeightGrams);

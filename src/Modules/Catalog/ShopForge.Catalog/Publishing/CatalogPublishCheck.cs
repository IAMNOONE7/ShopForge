using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tax;

namespace ShopForge.Catalog.Publishing;

internal sealed class CatalogPublishCheck(DbContext dbContext, ITaxRates taxRates, ICurrentStoreSettings storeSettings) : IStorePublishCheck
{
    public async Task<IReadOnlyList<StoreReadiness>> CheckAsync(CancellationToken cancellationToken)
    {
        var hasVisible = await dbContext.Set<StoreProduct>().AnyAsync(listing => listing.IsVisible, cancellationToken);

        return
        [
            hasVisible
                ? StoreReadiness.Ready("catalog.visible-products")
                : StoreReadiness.NotReady("catalog.visible-products", "The store has no visible products."),
            await TaxClassesAsync(cancellationToken),
            await MeasuredAsync(cancellationToken),
        ];
    }

    // A visible product with a form that has no weight, or no sizes, cannot be priced for delivery by weight and size,
    // so a store with one does not open (D-216). Nothing unmeasured can be created or shown any more, so only a
    // product from before that rule can be one.
    private async Task<StoreReadiness> MeasuredAsync(CancellationToken cancellationToken)
    {
        const string Check = "catalog.shipping-data";

        var unmeasuredProducts = dbContext.Set<ProductVariant>().Where(ProductVariant.Unmeasured).Select(variant => variant.ProductId);
        var unmeasured = await dbContext.Set<StoreProduct>()
            .Where(listing => listing.IsVisible && unmeasuredProducts.Contains(listing.ProductId))
            .OrderBy(listing => listing.Name)
            .Select(listing => listing.Name)
            .ToListAsync(cancellationToken);

        if (unmeasured.Count == 0)
        {
            return StoreReadiness.Ready(Check);
        }

        var named = string.Join(", ", unmeasured.Take(5)) + (unmeasured.Count > 5 ? $" and {unmeasured.Count - 5} more" : string.Empty);

        return StoreReadiness.NotReady(
            Check,
            $"{unmeasured.Count} visible {(unmeasured.Count == 1 ? "product has" : "products have")} a form without a weight or its length, width and height, so delivery cannot be priced for {(unmeasured.Count == 1 ? "it" : "them")}: {named}. Add them in the product or through the import, or hide {(unmeasured.Count == 1 ? "it" : "them")}.");
    }

    // A listing pointed at a tax class the shop has priced nowhere relevant is taxed at the rate the listing
    // itself carries — which is lawful and is almost certainly not what the merchant meant when they set the
    // class (D-187). Advice rather than a refusal: the fallback charges something sensible, and a shop with no
    // classes at all is the normal case and must not be nagged.
    private async Task<StoreReadiness> TaxClassesAsync(CancellationToken cancellationToken)
    {
        const string Check = "catalog.tax-classes";

        var named = await dbContext.Set<StoreProduct>()
            .Where(listing => listing.TaxClass != null)
            .Select(listing => listing.TaxClass!)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (named.Count == 0)
        {
            return StoreReadiness.Ready(Check, ReadinessWeight.Advised);
        }

        var home = (await storeSettings.GetAsync(cancellationToken)).Seller?.Country;
        var rates = await taxRates.ForAsync(destinationCountry: null, home, cancellationToken);
        var unpriced = named.Where(taxClass => !rates.ByClass.ContainsKey(taxClass)).Order(StringComparer.Ordinal).ToList();

        return unpriced.Count == 0
            ? StoreReadiness.Ready(Check, ReadinessWeight.Advised)
            : StoreReadiness.NotReady(
                Check,
                $"Listings are taxed as {string.Join(", ", unpriced)}, which {(unpriced.Count == 1 ? "has" : "have")} no rate for {home ?? "the store's own country"}. Those listings use the rate on the listing instead.",
                ReadinessWeight.Advised);
    }
}

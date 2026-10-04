using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Catalog;

namespace ShopForge.Catalog.Publishing;

internal sealed class SellableProductLookup(DbContext dbContext) : ISellableProducts
{
    public async Task<IReadOnlyList<SellableProduct>> FindAsync(IReadOnlyCollection<Guid> storeProductIds, CancellationToken cancellationToken)
    {
        var products = await (
                from storeProduct in dbContext.Set<StoreProduct>()
                where storeProductIds.Contains(storeProduct.Id) && storeProduct.IsVisible
                join product in dbContext.Set<Product>() on storeProduct.ProductId equals product.Id
                select new
                {
                    storeProduct.Id,
                    storeProduct.ProductId,
                    storeProduct.Name,
                    storeProduct.Slug,
                    storeProduct.Price,
                    storeProduct.VatRate,
                    ImageId = product.Images.OrderBy(image => image.Position).Select(image => (Guid?)image.Id).FirstOrDefault(),
                    product.OptionNames,
                    Variants = product.Variants
                        .OrderBy(variant => variant.Position)
                        .Select(variant => new SellableVariant(variant.Id, variant.Sku, variant.OptionValues, variant.Position, variant.WeightGrams))
                        .ToList(),
                })
            .ToListAsync(cancellationToken);

        return
        [
            .. products.Select(product => new SellableProduct(
                product.Id,
                product.ProductId,
                product.Name,
                product.Slug,
                product.Price,
                product.VatRate,
                product.ImageId is null ? null : $"/api/storefront/products/{product.Id}/images/{product.ImageId}",
                product.OptionNames,
                product.Variants)),
        ];
    }
}

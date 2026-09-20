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
                    storeProduct.Name,
                    storeProduct.Slug,
                    storeProduct.Price,
                    storeProduct.VatRate,
                    ImageId = product.Images.OrderBy(image => image.Position).Select(image => (Guid?)image.Id).FirstOrDefault(),
                })
            .ToListAsync(cancellationToken);

        return
        [
            .. products.Select(product => new SellableProduct(
                product.Id,
                product.Name,
                product.Slug,
                product.Price,
                product.VatRate,
                product.ImageId is null ? null : $"/api/storefront/products/{product.Id}/images/{product.ImageId}")),
        ];
    }
}

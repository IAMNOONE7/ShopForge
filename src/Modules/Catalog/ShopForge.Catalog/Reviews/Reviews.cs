using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Reviews;

internal sealed class ProductRatings(DbContext dbContext)
{
    // The average on the listing is whatever the published reviews say, recalculated where that changes (D-089).
    public async Task RecalculateAsync(Guid storeProductId, CancellationToken cancellationToken)
    {
        var published = await dbContext.Set<ProductReview>()
            .Where(review => review.StoreProductId == storeProductId && review.Status == ReviewStatus.Published)
            .Select(review => review.Rating)
            .ToListAsync(cancellationToken);

        var listing = await dbContext.Set<StoreProduct>().SingleAsync(storeProduct => storeProduct.Id == storeProductId, cancellationToken);

        listing.SetRating(
            published.Count == 0 ? 0m : decimal.Round((decimal)published.Average(), 2, MidpointRounding.AwayFromZero),
            published.Count);
    }
}

using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Reviews;
using ShopForge.Shared.Privacy;

namespace ShopForge.Catalog.Privacy;

// A review carries the name it was signed with, so erasing somebody takes their reviews with them — and the
// listing's average has to be counted again without them (D-117).
internal sealed class CustomerReviewData(DbContext dbContext, ProductRatings ratings) : ICustomerData
{
    public async Task<IReadOnlyList<CustomerDataSection>> ExportAsync(Guid storeCustomerId, CancellationToken cancellationToken)
    {
        var reviews = await dbContext.Set<ProductReview>()
            .AsNoTracking()
            .Where(review => review.StoreCustomerId == storeCustomerId)
            .OrderBy(review => review.WrittenAt)
            .Select(review => new ReviewExport(review.StoreProductId, review.Author, review.Rating, review.Text, review.WrittenAt, review.Status.ToString()))
            .ToListAsync(cancellationToken);

        return reviews.Count == 0 ? [] : [new CustomerDataSection("reviews", [.. reviews])];
    }

    public async Task EraseAsync(Guid storeCustomerId, CancellationToken cancellationToken)
    {
        var reviews = await dbContext.Set<ProductReview>()
            .Where(review => review.StoreCustomerId == storeCustomerId)
            .ToListAsync(cancellationToken);

        if (reviews.Count == 0)
        {
            return;
        }

        dbContext.RemoveRange(reviews);

        // The average is counted from what the database holds, so the reviews have to be gone before it is counted
        // again; the request's transaction is what keeps the two together.
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var storeProductId in reviews.Select(review => review.StoreProductId).Distinct())
        {
            await ratings.RecalculateAsync(storeProductId, cancellationToken);
        }
    }

    private sealed record ReviewExport(Guid StoreProductId, string Author, int Rating, string Text, DateTimeOffset WrittenAt, string Status);
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Reviews;

internal static class ReviewEndpoints
{
    public static void MapStorefrontReviews(this IEndpointRouteBuilder storefront)
    {
        storefront.MapGet("/products/{slug}/reviews", GetReviewsAsync);
        storefront.MapPost("/products/{slug}/reviews", WriteReviewAsync).RequireAuthorization(CustomerPolicies.Customer);
    }

    public static void MapAdminReviews(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapGet("/reviews", GetAllReviewsAsync);
        storeAdmin.MapPost("/reviews/{reviewId:guid}/publish", PublishAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
        storeAdmin.MapPost("/reviews/{reviewId:guid}/reject", RejectAsync).RequireAuthorization(AdminPolicies.CatalogManagement);
    }

    private static async Task<Results<Ok<ReviewsResponse>, NotFound>> GetReviewsAsync(
        string slug,
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        ICustomerPurchases purchases,
        CancellationToken cancellationToken)
    {
        var storeProductId = await ListingIdAsync(dbContext, slug, cancellationToken);

        if (storeProductId is null)
        {
            return TypedResults.NotFound();
        }

        // A review nobody has published is not part of the shop yet, whoever wrote it.
        var reviews = await dbContext.Set<ProductReview>()
            .AsNoTracking()
            .Where(review => review.StoreProductId == storeProductId && review.Status == ReviewStatus.Published)
            .OrderByDescending(review => review.WrittenAt)
            .Select(review => new ReviewResponse(review.Author, review.Rating, review.Text, review.WrittenAt))
            .ToListAsync(cancellationToken);

        var canWrite = await CanWriteAsync(storeProductId.Value, dbContext, currentCustomer, purchases, cancellationToken);

        return TypedResults.Ok(new ReviewsResponse(canWrite, reviews));
    }

    // The shop only offers the form to someone whose review would be taken, which is the same question the
    // POST answers with 403 and 409.
    private static async Task<bool> CanWriteAsync(
        Guid storeProductId,
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        ICustomerPurchases purchases,
        CancellationToken cancellationToken)
    {
        if (await currentCustomer.FindAsync(cancellationToken) is not { StoreCustomerId: var storeCustomerId })
        {
            return false;
        }

        return await purchases.HasBoughtAsync(storeCustomerId, storeProductId, cancellationToken)
            && !await HasReviewedAsync(dbContext, storeProductId, storeCustomerId, cancellationToken);
    }

    private static async Task<Results<Accepted, ValidationProblem, NotFound, ForbidHttpResult, ProblemHttpResult>> WriteReviewAsync(
        string slug,
        ReviewRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        ICurrentCustomer currentCustomer,
        ICustomerPurchases purchases,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(request.Rating is >= 1 and <= 5, "rating", "A rating is between one and five stars.")
            .Check(
                !string.IsNullOrWhiteSpace(request.Text) && request.Text.Trim().Length <= ProductReview.MaxTextLength,
                "text",
                $"Say something about the product, in up to {ProductReview.MaxTextLength} characters.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var storeProductId = await ListingIdAsync(dbContext, slug, cancellationToken);

        if (storeProductId is null)
        {
            return TypedResults.NotFound();
        }

        if (await currentCustomer.FindAsync(cancellationToken) is not { StoreCustomerId: var storeCustomerId })
        {
            return TypedResults.Forbid();
        }

        if (!await purchases.HasBoughtAsync(storeCustomerId, storeProductId.Value, cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Only a customer who bought this product can review it",
                detail: "Reviews come from orders, so that a rating means something.");
        }

        if (await HasReviewedAsync(dbContext, storeProductId.Value, storeCustomerId, cancellationToken))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "You have already reviewed this product");
        }

        dbContext.Add(new ProductReview(
            storeContext.StoreId!.Value,
            storeProductId.Value,
            storeCustomerId,
            request.Author is { Length: > 0 } author ? author : "A customer",
            request.Rating,
            request.Text!,
            clock.GetUtcNow()));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Ok<List<AdminReviewResponse>>> GetAllReviewsAsync(
        string? status,
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var wanted = Enum.TryParse<ReviewStatus>(status, ignoreCase: true, out var parsed) ? parsed : (ReviewStatus?)null;

        var reviews = await (
                from review in dbContext.Set<ProductReview>().AsNoTracking()
                where wanted == null || review.Status == wanted
                join storeProduct in dbContext.Set<StoreProduct>() on review.StoreProductId equals storeProduct.Id
                orderby review.WrittenAt descending
                select new AdminReviewResponse(
                    review.Id,
                    storeProduct.Name,
                    storeProduct.Slug,
                    review.Author,
                    review.Rating,
                    review.Text,
                    review.WrittenAt,
                    review.Status.ToString()))
            .Take(200)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(reviews);
    }

    private static Task<Results<NoContent, NotFound>> PublishAsync(
        Guid reviewId,
        DbContext dbContext,
        ProductRatings ratings,
        CancellationToken cancellationToken) =>
        ModerateAsync(reviewId, review => review.Publish(), dbContext, ratings, cancellationToken);

    private static Task<Results<NoContent, NotFound>> RejectAsync(
        Guid reviewId,
        DbContext dbContext,
        ProductRatings ratings,
        CancellationToken cancellationToken) =>
        ModerateAsync(reviewId, review => review.Reject(), dbContext, ratings, cancellationToken);

    private static async Task<Results<NoContent, NotFound>> ModerateAsync(
        Guid reviewId,
        Func<ProductReview, bool> decide,
        DbContext dbContext,
        ProductRatings ratings,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.Set<ProductReview>().SingleOrDefaultAsync(candidate => candidate.Id == reviewId, cancellationToken);

        if (review is null)
        {
            return TypedResults.NotFound();
        }

        // The decision is written before the average is worked out, because the average is read back from the
        // published reviews — and the two are saved together, so a listing never disagrees with its reviews (D-089).
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        decide(review);
        await dbContext.SaveChangesAsync(cancellationToken);
        await ratings.RecalculateAsync(review.StoreProductId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static Task<bool> HasReviewedAsync(
        DbContext dbContext,
        Guid storeProductId,
        Guid storeCustomerId,
        CancellationToken cancellationToken) =>
        dbContext.Set<ProductReview>()
            .AnyAsync(review => review.StoreProductId == storeProductId && review.StoreCustomerId == storeCustomerId, cancellationToken);

    private static Task<Guid?> ListingIdAsync(DbContext dbContext, string slug, CancellationToken cancellationToken) =>
        dbContext.Set<StoreProduct>()
            .Where(storeProduct => storeProduct.Slug == slug && storeProduct.IsVisible)
            .Select(storeProduct => (Guid?)storeProduct.Id)
            .SingleOrDefaultAsync(cancellationToken);
}

internal sealed record ReviewRequest(int Rating, string? Text, string? Author);

internal sealed record ReviewsResponse(bool CanWrite, List<ReviewResponse> Reviews);

internal sealed record ReviewResponse(string Author, int Rating, string Text, DateTimeOffset WrittenAt);

internal sealed record AdminReviewResponse(
    Guid Id,
    string ProductName,
    string ProductSlug,
    string Author,
    int Rating,
    string Text,
    DateTimeOffset WrittenAt,
    string Status);

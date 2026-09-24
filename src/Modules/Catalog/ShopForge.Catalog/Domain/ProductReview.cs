using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

internal sealed class ProductReview : IStoreOwned
{
    public const int MaxTextLength = 2000;

    private ProductReview()
    {
    }

    public ProductReview(Guid storeId, Guid storeProductId, Guid storeCustomerId, string author, int rating, string text, DateTimeOffset writtenAt)
    {
        if (rating is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "A rating is between one and five stars.");
        }

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        StoreProductId = storeProductId;
        StoreCustomerId = storeCustomerId;
        Author = author.Trim();
        Rating = rating;
        Text = text.Trim();
        WrittenAt = writtenAt;
        Status = ReviewStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid StoreProductId { get; private set; }

    public Guid StoreCustomerId { get; private set; }

    // The name as it was when the review was written; a customer renaming themselves later does not rewrite history.
    public string Author { get; private set; } = null!;

    public int Rating { get; private set; }

    public string Text { get; private set; } = null!;

    public DateTimeOffset WrittenAt { get; private set; }

    public ReviewStatus Status { get; private set; }

    public bool Publish()
    {
        if (Status == ReviewStatus.Published)
        {
            return false;
        }

        Status = ReviewStatus.Published;

        return true;
    }

    // A rejected review keeps its row, so the same customer cannot write another one to get around it (D-089).
    public bool Reject()
    {
        if (Status == ReviewStatus.Rejected)
        {
            return false;
        }

        Status = ReviewStatus.Rejected;

        return true;
    }
}

internal enum ReviewStatus
{
    Pending,
    Published,
    Rejected,
}

using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

// Where a page used to live. A slug is part of an address somebody has written down — in a search index, in a
// message, on paper — so renaming one must leave a trail rather than a hole (D-166). It can only be recorded
// from the moment this exists; every rename before it is a link already lost.
internal sealed class SlugHistory : IStoreOwned
{
    private SlugHistory()
    {
    }

    public SlugHistory(Guid storeId, SlugKind kind, string slug, Guid pointsAt, DateTimeOffset movedAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Kind = kind;
        Slug = slug;
        PointsAt = pointsAt;
        MovedAt = movedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public SlugKind Kind { get; private set; }

    public string Slug { get; private set; } = null!;

    // The row, not the slug it moved to: two renames in a row then lead to wherever that row is called now,
    // rather than to a middle name that no longer answers either.
    public Guid PointsAt { get; private set; }

    public DateTimeOffset MovedAt { get; private set; }

    public void NowPointsAt(Guid target, DateTimeOffset movedAt)
    {
        PointsAt = target;
        MovedAt = movedAt;
    }
}

internal enum SlugKind
{
    Listing,
    Category,
}

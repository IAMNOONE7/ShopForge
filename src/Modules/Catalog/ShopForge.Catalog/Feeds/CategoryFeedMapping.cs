using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Feeds;

// What one shopping engine calls one of a shop's categories. The engine's taxonomy is not the merchant's, and
// nobody can map between them honestly by machine, so the merchant does it once and the platform keeps it —
// in one table for every engine rather than one table each (D-147, D-170).
internal sealed class CategoryFeedMapping : IStoreOwned
{
    public const int MaxCategoryLength = 300;

    private CategoryFeedMapping()
    {
    }

    public CategoryFeedMapping(Guid storeId, Guid categoryId, string feed, string engineCategory)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        CategoryId = categoryId;
        Feed = feed;
        EngineCategory = engineCategory.Trim();
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Feed { get; private set; } = null!;

    // The engine's own words, as the merchant copied them out of the engine's own list.
    public string EngineCategory { get; private set; } = null!;

    public void Rename(string engineCategory) => EngineCategory = engineCategory.Trim();
}

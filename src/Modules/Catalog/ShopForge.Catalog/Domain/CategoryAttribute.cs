using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

internal sealed class CategoryAttribute : IStoreOwned
{
    private CategoryAttribute()
    {
    }

    internal CategoryAttribute(Guid storeId, Guid categoryId, Guid attributeDefinitionId, int sortOrder)
    {
        StoreId = storeId;
        CategoryId = categoryId;
        AttributeDefinitionId = attributeDefinitionId;
        SortOrder = sortOrder;
    }

    public Guid StoreId { get; private set; }

    public Guid CategoryId { get; private set; }

    public Guid AttributeDefinitionId { get; private set; }

    public int SortOrder { get; private set; }

    internal void MoveTo(int sortOrder) => SortOrder = sortOrder;
}

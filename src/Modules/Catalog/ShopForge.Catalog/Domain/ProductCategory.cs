using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

internal sealed class ProductCategory : IStoreOwned
{
    private ProductCategory()
    {
    }

    internal ProductCategory(Guid storeId, Guid storeProductId, Guid categoryId)
    {
        StoreId = storeId;
        StoreProductId = storeProductId;
        CategoryId = categoryId;
    }

    public Guid StoreId { get; private set; }

    public Guid StoreProductId { get; private set; }

    public Guid CategoryId { get; private set; }
}

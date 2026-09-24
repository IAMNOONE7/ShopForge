using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Domain;

internal sealed class WishlistItem : IStoreOwned
{
    private WishlistItem()
    {
    }

    public WishlistItem(Guid storeId, Guid storeCustomerId, Guid storeProductId, DateTimeOffset addedAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        StoreCustomerId = storeCustomerId;
        StoreProductId = storeProductId;
        AddedAt = addedAt;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid StoreCustomerId { get; private set; }

    public Guid StoreProductId { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }
}

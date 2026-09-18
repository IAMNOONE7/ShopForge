namespace ShopForge.Shared.Tenancy;

public interface IStoreContext
{
    Guid? StoreId { get; }

    Guid? TenantId { get; }
}

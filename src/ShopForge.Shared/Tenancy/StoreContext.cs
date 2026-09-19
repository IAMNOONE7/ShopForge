namespace ShopForge.Shared.Tenancy;

public sealed class StoreContext : IStoreContext
{
    public Guid? StoreId { get; private set; }

    public Guid? TenantId { get; private set; }

    public void SetTenant(Guid tenantId)
    {
        if (TenantId is not null && TenantId != tenantId)
        {
            throw new InvalidOperationException("The store context is already set to a different tenant.");
        }

        TenantId = tenantId;
    }

    public void Set(Guid storeId, Guid tenantId)
    {
        if (StoreId is not null && StoreId != storeId)
        {
            throw new InvalidOperationException("The store context is already set to a different store.");
        }

        SetTenant(tenantId);
        StoreId = storeId;
    }
}

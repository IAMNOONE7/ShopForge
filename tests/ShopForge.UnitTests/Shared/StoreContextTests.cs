using ShopForge.Shared.Tenancy;

namespace ShopForge.UnitTests.Shared;

public sealed class StoreContextTests
{
    [Fact]
    public void Setting_the_same_store_again_is_allowed()
    {
        var storeId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var context = new StoreContext();

        context.Set(storeId, tenantId);
        context.Set(storeId, tenantId);

        Assert.Equal(storeId, context.StoreId);
        Assert.Equal(tenantId, context.TenantId);
    }

    [Fact]
    public void Switching_to_another_store_is_rejected()
    {
        var context = new StoreContext();
        context.Set(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => context.Set(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Tenant_context_can_be_narrowed_to_a_store_of_the_same_tenant()
    {
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var context = new StoreContext();

        context.SetTenant(tenantId);
        context.Set(storeId, tenantId);

        Assert.Equal(storeId, context.StoreId);
        Assert.Equal(tenantId, context.TenantId);
    }

    [Fact]
    public void Store_of_another_tenant_is_rejected()
    {
        var context = new StoreContext();
        context.SetTenant(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => context.Set(Guid.NewGuid(), Guid.NewGuid()));
    }
}

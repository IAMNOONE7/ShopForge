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
}

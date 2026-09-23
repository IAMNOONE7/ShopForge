using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.IntegrationTests;

internal sealed record TestStore(Guid StoreId, Guid TenantId, string Name, string HostName, Guid DomainId);

internal static class TestStores
{
    public static string UniqueHostName(string label = "shop") => $"{label}-{Guid.NewGuid():N}.test";

    public static async Task<(TestStore A, TestStore B)> CreateTwoStoresOfOneTenantAsync(IServiceProvider services)
    {
        var tenant = new Tenant("Test tenant");
        var storeA = await CreateAsync(services, tenant, "Store A", addTenant: true);
        var storeB = await CreateAsync(services, tenant, "Store B", addTenant: false);

        return (storeA, storeB);
    }

    public static AsyncServiceScope CreateScope(IServiceProvider services, TestStore? store)
    {
        var scope = services.CreateAsyncScope();

        if (store is not null)
        {
            scope.ServiceProvider.GetRequiredService<StoreContext>().Set(store.StoreId, store.TenantId);
        }

        return scope;
    }

    private static async Task<TestStore> CreateAsync(IServiceProvider services, Tenant tenant, string name, bool addTenant)
    {
        var store = new Store(tenant.Id, name, "EUR", "en-IE", new StoreTheme("#112233", "#FFFFFF", 4));
        var domain = store.AddDomain(UniqueHostName());
        store.SetCompany(new StoreCompany("Test Furniture s.r.o.", "1 Workshop Lane", "Brno", "602 00", "CZ", "12345678", "CZ12345678"));
        store.Publish();

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(store.Id, store.TenantId);

        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        if (addTenant)
        {
            dbContext.Add(tenant);
        }

        dbContext.Add(store);
        dbContext.Add(new PaymentMethod(store.Id, "bank-transfer", "Bank transfer", "manual"));
        dbContext.Add(new ShippingMethod(store.Id, "courier", "Courier", "manual", price: 4.90m, vatRate: 21m));
        await dbContext.SaveChangesAsync();

        return new TestStore(store.Id, store.TenantId, store.Name, domain.HostName, domain.Id);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.IntegrationTests;

internal sealed record TestStore(Guid StoreId, Guid TenantId, string Name, string HostName, Guid DomainId);

internal static class TestStores
{
    public static string UniqueHostName(string label = "shop") => $"{label}-{Guid.NewGuid():N}.test";

    // A shop that charges in something other than euros, for the money that does not have two decimals (D-186).
    public static Task<TestStore> CreateInCurrencyAsync(IServiceProvider services, string currency) =>
        CreateAsync(services, new Tenant("Test tenant"), $"{currency} store", addTenant: true, currency);

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

    private static async Task<TestStore> CreateAsync(IServiceProvider services, Tenant tenant, string name, bool addTenant, string code = "EUR")
    {
        var currency = Currency.Of(code);
        var store = new Store(tenant.Id, name, code, "en-IE", new StoreTheme("#112233", "#FFFFFF", 4));
        var domain = store.AddDomain(UniqueHostName(), DateTimeOffset.UtcNow);
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
        dbContext.Add(new ShippingMethod(store.Id, "courier", "Courier", "manual", currency.Round(4.90m), vatRate: 21m, currency));
        await dbContext.SaveChangesAsync();

        return new TestStore(store.Id, store.TenantId, store.Name, domain.HostName, domain.Id);
    }
}

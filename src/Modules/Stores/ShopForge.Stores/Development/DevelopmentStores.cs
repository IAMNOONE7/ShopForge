using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Development;

public static class DevelopmentStores
{
    public const string DemoTenantName = "Demo Retail";

    public static async Task<Guid> SeedDevelopmentStoresAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using (var scope = services.CreateAsyncScope())
        {
            var existingTenantId = await scope.ServiceProvider.GetRequiredService<DbContext>().Set<Tenant>()
                .Where(tenant => tenant.Name == DemoTenantName)
                .Select(tenant => (Guid?)tenant.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingTenantId is not null)
            {
                return existingTenantId.Value;
            }
        }

        var tenant = new Tenant(DemoTenantName);

        var woodenHome = new Store(tenant.Id, "Wooden Home", "CZK", "cs-CZ", new StoreTheme("#8B5A2B", "#F5F0E8", 8));
        woodenHome.AddDomain("shop-a.localhost");

        var voltElectronics = new Store(tenant.Id, "Volt Electronics", "EUR", "en-IE", new StoreTheme("#1F6FEB", "#EEF4FF", 2));
        voltElectronics.AddDomain("shop-b.localhost");

        await SaveAsync(services, woodenHome, [tenant, woodenHome], cancellationToken);
        await SaveAsync(services, voltElectronics, [voltElectronics], cancellationToken);

        return tenant.Id;
    }

    private static async Task SaveAsync(IServiceProvider services, Store store, object[] entities, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(store.Id, store.TenantId);

        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        dbContext.AddRange(entities);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

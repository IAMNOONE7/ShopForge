using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Development;

public static class DevelopmentStores
{
    public static async Task SeedDevelopmentStoresAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using (var scope = services.CreateAsyncScope())
        {
            if (await scope.ServiceProvider.GetRequiredService<DbContext>().Set<Tenant>().AnyAsync(cancellationToken))
            {
                return;
            }
        }

        var tenant = new Tenant("Demo Retail");

        var woodenHome = new Store(tenant.Id, "Wooden Home", "CZK", "cs-CZ", new StoreTheme("#8B5A2B", "#F5F0E8", 8));
        woodenHome.AddDomain("shop-a.localhost");

        var voltElectronics = new Store(tenant.Id, "Volt Electronics", "EUR", "en-IE", new StoreTheme("#1F6FEB", "#EEF4FF", 2));
        voltElectronics.AddDomain("shop-b.localhost");

        await SaveAsync(services, woodenHome, [tenant, woodenHome], cancellationToken);
        await SaveAsync(services, voltElectronics, [voltElectronics], cancellationToken);
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

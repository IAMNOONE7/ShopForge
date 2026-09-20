using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Development;

public static class DevelopmentStores
{
    private const string DemoTenantName = "Demo Retail";
    private const string WoodenHomeHost = "shop-a.localhost";
    private const string VoltElectronicsHost = "shop-b.localhost";

    public static async Task<DevelopmentStoreIds> SeedDevelopmentStoresAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using (var scope = services.CreateAsyncScope())
        {
            var existing = await scope.ServiceProvider.GetRequiredService<DbContext>().Set<Store>()
                .IgnoreQueryFilters()
                .Where(store => store.Domains.Any(domain => domain.HostName == WoodenHomeHost || domain.HostName == VoltElectronicsHost))
                .Select(store => new { store.Id, store.TenantId, store.Name })
                .ToListAsync(cancellationToken);

            if (existing.Count == 2)
            {
                return new DevelopmentStoreIds(
                    existing[0].TenantId,
                    existing.Single(store => store.Name == "Wooden Home").Id,
                    existing.Single(store => store.Name == "Volt Electronics").Id);
            }
        }

        var tenant = new Tenant(DemoTenantName);

        var woodenHome = new Store(tenant.Id, "Wooden Home", "CZK", "cs-CZ", new StoreTheme("#8B5A2B", "#F5F0E8", 8));
        woodenHome.AddDomain(WoodenHomeHost);
        woodenHome.Publish();

        var voltElectronics = new Store(tenant.Id, "Volt Electronics", "EUR", "en-IE", new StoreTheme("#1F6FEB", "#EEF4FF", 2));
        voltElectronics.AddDomain(VoltElectronicsHost);
        voltElectronics.Publish();

        await SaveAsync(services, woodenHome, [tenant, woodenHome], cancellationToken);
        await SaveAsync(services, voltElectronics, [voltElectronics], cancellationToken);

        return new DevelopmentStoreIds(tenant.Id, woodenHome.Id, voltElectronics.Id);
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

public sealed record DevelopmentStoreIds(Guid TenantId, Guid WoodenHomeStoreId, Guid VoltElectronicsStoreId);

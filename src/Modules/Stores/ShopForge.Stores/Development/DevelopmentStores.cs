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
                await AddMissingCompaniesAsync(services, existing.Select(store => (store.Id, store.TenantId)), cancellationToken);

                return new DevelopmentStoreIds(
                    existing[0].TenantId,
                    existing.Single(store => store.Name == "Wooden Home").Id,
                    existing.Single(store => store.Name == "Volt Electronics").Id);
            }
        }

        var now = TimeProvider.System.GetUtcNow();
        var tenant = new Tenant(DemoTenantName);

        var woodenHome = new Store(tenant.Id, "Wooden Home", "CZK", "cs-CZ", new StoreTheme("#8B5A2B", "#F5F0E8", 8));
        woodenHome.AddDomain(WoodenHomeHost, now);
        woodenHome.SetCompany(new StoreCompany("Wooden Home s.r.o.", "Dřevařská 12", "Brno", "602 00", "CZ", "27654321", "CZ27654321"));
        woodenHome.Publish();

        var voltElectronics = new Store(tenant.Id, "Volt Electronics", "EUR", "en-IE", new StoreTheme("#1F6FEB", "#EEF4FF", 2));
        voltElectronics.AddDomain(VoltElectronicsHost, now);
        voltElectronics.SetCompany(new StoreCompany("Volt Electronics Ltd", "4 Dock Road", "Galway", "H91 AB12", "IE", "IE448921", "IE4489217W"));
        voltElectronics.Publish();

        await SaveAsync(services, woodenHome, [tenant, woodenHome], cancellationToken);
        await SaveAsync(services, voltElectronics, [voltElectronics], cancellationToken);

        return new DevelopmentStoreIds(tenant.Id, woodenHome.Id, voltElectronics.Id);
    }

    // Stores seeded before company details existed keep working: invoices need a seller (D-078).
    private static async Task AddMissingCompaniesAsync(
        IServiceProvider services,
        IEnumerable<(Guid Id, Guid TenantId)> stores,
        CancellationToken cancellationToken)
    {
        foreach (var (id, tenantId) in stores)
        {
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<StoreContext>().Set(id, tenantId);

            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            var store = await dbContext.Set<Store>().SingleAsync(candidate => candidate.Id == id, cancellationToken);

            if (store.Company is not null)
            {
                continue;
            }

            store.SetCompany(store.Name == "Wooden Home"
                ? new StoreCompany("Wooden Home s.r.o.", "Dřevařská 12", "Brno", "602 00", "CZ", "27654321", "CZ27654321")
                : new StoreCompany("Volt Electronics Ltd", "4 Dock Road", "Galway", "H91 AB12", "IE", "IE448921", "IE4489217W"));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
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

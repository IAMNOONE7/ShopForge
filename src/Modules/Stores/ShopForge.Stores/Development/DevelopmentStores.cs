using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Development;

public static class DevelopmentStores
{
    private const string DemoTenantName = "Demo Retail";
    // The labels, not the whole names: the suffix is configuration, so the seed composes through the same
    // place store creation does (D-189). On this machine that makes `shop-a.localhost`.
    private const string WoodenHomeLabel = "shop-a";
    private const string VoltElectronicsLabel = "shop-b";

    // A Czech tool shop, the search's test corpus (D-225), so the live checks and the screens see what the tests see.
    private const string ToolShopLabel = "shop-c";
    private const string ToolShopName = "Železářství Kladívko";

    public static async Task<DevelopmentStoreIds> SeedDevelopmentStoresAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var platformHosts = services.GetRequiredService<PlatformHosts>();
        var woodenHomeHost = platformHosts.Compose(WoodenHomeLabel);
        var voltElectronicsHost = platformHosts.Compose(VoltElectronicsLabel);
        var toolShopHost = platformHosts.Compose(ToolShopLabel);
        var now = TimeProvider.System.GetUtcNow();

        await using (var scope = services.CreateAsyncScope())
        {
            var existing = await scope.ServiceProvider.GetRequiredService<DbContext>().Set<Store>()
                .IgnoreQueryFilters()
                .Where(store => store.Domains.Any(domain =>
                    domain.HostName == woodenHomeHost || domain.HostName == voltElectronicsHost || domain.HostName == toolShopHost))
                .Select(store => new { store.Id, store.TenantId, store.Name })
                .ToListAsync(cancellationToken);
            var demo = existing.Where(store => store.Name is "Wooden Home" or "Volt Electronics").ToList();

            if (demo.Count == 2)
            {
                await AddMissingCompaniesAsync(services, demo.Select(store => (store.Id, store.TenantId)), cancellationToken);

                // A database seeded before the tool shop existed gains it beside the other two.
                var toolShop = existing.SingleOrDefault(store => store.Name == ToolShopName)?.Id;

                if (toolShop is null)
                {
                    var added = ToolShop(demo[0].TenantId, platformHosts, now);
                    await SaveAsync(services, added, [added], cancellationToken);
                    toolShop = added.Id;
                }

                return new DevelopmentStoreIds(
                    demo[0].TenantId,
                    demo.Single(store => store.Name == "Wooden Home").Id,
                    demo.Single(store => store.Name == "Volt Electronics").Id,
                    toolShop.Value);
            }
        }

        var tenant = new Tenant(DemoTenantName);

        var woodenHome = new Store(tenant.Id, "Wooden Home", "CZK", "cs-CZ", new StoreTheme("#8B5A2B", "#F5F0E8", 8));
        woodenHome.AddPlatformDomain(platformHosts, WoodenHomeLabel, now);
        woodenHome.SetCompany(new StoreCompany("Wooden Home s.r.o.", "Dřevařská 12", "Brno", "602 00", "CZ", "27654321", "CZ27654321"));
        woodenHome.Publish();

        var voltElectronics = new Store(tenant.Id, "Volt Electronics", "EUR", "en-IE", new StoreTheme("#1F6FEB", "#EEF4FF", 2));
        voltElectronics.AddPlatformDomain(platformHosts, VoltElectronicsLabel, now);
        voltElectronics.SetCompany(new StoreCompany("Volt Electronics Ltd", "4 Dock Road", "Galway", "H91 AB12", "IE", "IE448921", "IE4489217W"));
        voltElectronics.Publish();

        var toolShopStore = ToolShop(tenant.Id, platformHosts, now);

        await SaveAsync(services, woodenHome, [tenant, woodenHome], cancellationToken);
        await SaveAsync(services, voltElectronics, [voltElectronics], cancellationToken);
        await SaveAsync(services, toolShopStore, [toolShopStore], cancellationToken);

        return new DevelopmentStoreIds(tenant.Id, woodenHome.Id, voltElectronics.Id, toolShopStore.Id);
    }

    private static Store ToolShop(Guid tenantId, PlatformHosts platformHosts, DateTimeOffset now)
    {
        var store = new Store(tenantId, ToolShopName, "CZK", "cs-CZ", new StoreTheme("#9A3412", "#FFF7ED", 4));
        store.AddPlatformDomain(platformHosts, ToolShopLabel, now);
        store.SetCompany(new StoreCompany("Železářství Kladívko s.r.o. (test)", "Kovářská 7", "Brno", "612 00", "CZ", "27654330", "CZ27654330"));
        store.Publish();

        return store;
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

public sealed record DevelopmentStoreIds(Guid TenantId, Guid WoodenHomeStoreId, Guid VoltElectronicsStoreId, Guid ToolShopStoreId);

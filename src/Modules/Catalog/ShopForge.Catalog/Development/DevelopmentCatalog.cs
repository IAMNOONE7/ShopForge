using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Development;

public static class DevelopmentCatalog
{
    public static async Task SeedDevelopmentCatalogAsync(
        this IServiceProvider services,
        Guid tenantId,
        Guid woodenHomeStoreId,
        Guid voltElectronicsStoreId,
        CancellationToken cancellationToken = default)
    {
        await using (var scope = services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<StoreContext>().SetTenant(tenantId);

            if (await scope.ServiceProvider.GetRequiredService<DbContext>().Set<Product>().AnyAsync(cancellationToken))
            {
                return;
            }
        }

        await SeedStoreAsync(services, tenantId, woodenHomeStoreId, "Cutting Boards",
        [
            ("WH-BOARD-OAK", "Oak Kitchen Cutting Board", "Solid oak board, oiled with food-safe oil.", 499m),
            ("WH-BOARD-WALNUT", "Walnut Serving Board", "End-grain walnut with juice groove.", 890m),
            ("WH-TRAY-BEECH", "Beech Serving Tray", "Lightweight beech tray with handles.", 649m),
        ], cancellationToken);

        await SeedStoreAsync(services, tenantId, voltElectronicsStoreId, "Chargers",
        [
            ("VE-USBC-65W", "USB-C Charger 65 W", "GaN charger for laptops and phones.", 39.90m),
            ("VE-CABLE-2M", "USB-C Cable 2 m", "Braided cable, 100 W power delivery.", 14.90m),
        ], cancellationToken);
    }

    private static async Task SeedStoreAsync(
        IServiceProvider services,
        Guid tenantId,
        Guid storeId,
        string categoryName,
        (string Sku, string Name, string Description, decimal Price)[] items,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(storeId, tenantId);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        var category = new Category(storeId, categoryName, Slugs.Create(categoryName), sortOrder: 0);
        dbContext.Add(category);

        foreach (var (sku, name, description, price) in items)
        {
            var product = new Product(tenantId, sku, ean: null, weightGrams: null);
            var storeProduct = new StoreProduct(storeId, product, new StoreProductDetails(name, Slugs.Create(name), description, price, IsVisible: true, SortOrder: 0));
            storeProduct.AssignCategories([category]);
            dbContext.AddRange(product, storeProduct);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

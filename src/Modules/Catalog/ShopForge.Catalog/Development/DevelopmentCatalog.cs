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
        bool hasProducts;
        await using (var scope = services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<StoreContext>().SetTenant(tenantId);
            hasProducts = await scope.ServiceProvider.GetRequiredService<DbContext>().Set<Product>().AnyAsync(cancellationToken);
        }

        if (!hasProducts)
        {
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

        await SeedAttributesAsync(services, tenantId, woodenHomeStoreId, "cutting-boards",
            [
                Attribute("material", "Material", AttributeType.Select, unit: null, "Oak", "Walnut", "Beech"),
                Attribute("length", "Length", AttributeType.Decimal, "cm"),
                Attribute("dishwasher-safe", "Dishwasher safe", AttributeType.Boolean, unit: null),
            ],
            new()
            {
                ["oak-kitchen-cutting-board"] = new() { ["material"] = new[] { "oak" }, ["length"] = 40m, ["dishwasher-safe"] = false },
                ["walnut-serving-board"] = new() { ["material"] = new[] { "walnut" }, ["length"] = 55m, ["dishwasher-safe"] = false },
                ["beech-serving-tray"] = new() { ["material"] = new[] { "beech" }, ["length"] = 48.5m, ["dishwasher-safe"] = true },
            },
            cancellationToken);

        await SeedAttributesAsync(services, tenantId, voltElectronicsStoreId, "chargers",
            [
                Attribute("power", "Power", AttributeType.Integer, "W"),
                Attribute("connectors", "Connectors", AttributeType.MultiSelect, unit: null, "USB-C", "USB-A", "Lightning"),
                Attribute("cable-length", "Cable length", AttributeType.Decimal, "m"),
            ],
            new()
            {
                ["usb-c-charger-65-w"] = new() { ["power"] = 65L, ["connectors"] = new[] { "usb-c", "usb-a" } },
                ["usb-c-cable-2-m"] = new() { ["power"] = 100L, ["connectors"] = new[] { "usb-c" }, ["cable-length"] = 2m },
            },
            cancellationToken);
    }

    private static (string Code, string Name, AttributeType Type, string? Unit, string[] Options) Attribute(
        string code, string name, AttributeType type, string? unit, params string[] options) => (code, name, type, unit, options);

    private static async Task SeedAttributesAsync(
        IServiceProvider services,
        Guid tenantId,
        Guid storeId,
        string categorySlug,
        (string Code, string Name, AttributeType Type, string? Unit, string[] Options)[] attributes,
        Dictionary<string, Dictionary<string, object>> valuesBySlug,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(storeId, tenantId);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        if (await dbContext.Set<AttributeDefinition>().AnyAsync(cancellationToken))
        {
            return;
        }

        var definitions = attributes.Select((attribute, index) =>
        {
            var definition = new AttributeDefinition(storeId, attribute.Code, attribute.Name, attribute.Type,
                new AttributeSettings(attribute.Unit, IsFilterable: true, IsVisibleOnProductPage: true, SortOrder: index));

            foreach (var option in attribute.Options)
            {
                definition.AddOption(option, Slugs.Create(option));
            }

            return definition;
        }).ToList();
        dbContext.AddRange(definitions);

        var category = await dbContext.Set<Category>().Include(category => category.Attributes).SingleAsync(category => category.Slug == categorySlug, cancellationToken);
        category.AssignAttributes(definitions);

        var storeProducts = await dbContext.Set<StoreProduct>()
            .Include(storeProduct => storeProduct.AttributeValues)
            .Where(storeProduct => valuesBySlug.Keys.Contains(storeProduct.Slug))
            .ToListAsync(cancellationToken);

        foreach (var storeProduct in storeProducts)
        {
            storeProduct.ReplaceAttributeValues(
            [
                .. valuesBySlug[storeProduct.Slug].Select(item => ToValue(definitions.Single(definition => definition.Code == item.Key), item.Value)),
            ]);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static (AttributeDefinition, AttributeValue) ToValue(AttributeDefinition definition, object value) => (definition, value switch
    {
        string[] codes => new AttributeValue { OptionIds = [.. codes.Select(code => definition.Options.Single(option => option.Code == code).Id)] },
        long number => new AttributeValue { Integer = number },
        decimal number => new AttributeValue { Decimal = number },
        bool flag => new AttributeValue { Boolean = flag },
        _ => throw new ArgumentException($"Unsupported seed value for '{definition.Code}'.", nameof(value)),
    });

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

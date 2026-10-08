using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Development;

public static class DevelopmentCatalog
{
    // Enough of a catalogue to browse, filter and sort through, in two stores that sell nothing alike. Seeding adds
    // what is missing rather than all or nothing, so a database from an earlier run picks up whatever was added since.
    private static readonly SeedCategory[] WoodenHome =
    [
        new("Cutting Boards",
        [
            new("WH-BOARD-OAK", "Oak Kitchen Cutting Board", "Solid oak board, oiled with food-safe oil.", 499m),
            new("WH-BOARD-WALNUT", "Walnut Serving Board", "End-grain walnut with juice groove.", 890m),
            new("WH-TRAY-BEECH", "Beech Serving Tray", "Lightweight beech tray with handles.", 649m),
            new("WH-BOARD-OAK-XL", "Oak Carving Board XL", "Wide oak board with a channel for the juices.", 1290m),
            new("WH-BOARD-BEECH-ROUND", "Round Beech Board", "Round board for bread and cheese.", 390m),
            new("WH-BOARD-WALNUT-MINI", "Walnut Cheese Board", "Small walnut board for the table.", 590m),
        ]),
        new("Tables",
        [
            new("WH-TABLE-OAK-DINING", "Oak Dining Table", "Six-seat table on tapered legs.", 18900m),
            new("WH-TABLE-SIDE-BEECH", "Beech Side Table", "Low table for beside a chair.", 3490m),
            new("WH-DESK-WALNUT", "Walnut Writing Desk", "Desk with one drawer and a cable channel.", 12900m),
        ]),
        new("Storage",
        [
            new("WH-SHELF-OAK", "Oak Wall Shelf", "Floating shelf with hidden fixings.", 2290m),
            new("WH-BOX-BEECH", "Beech Storage Box", "Stackable box with a lid.", 890m),
            new("WH-RACK-WALNUT", "Walnut Spice Rack", "Two tiers, fits above the worktop.", 1490m),
        ]),
    ];

    private static readonly SeedCategory[] VoltElectronics =
    [
        new("Chargers",
        [
            new("VE-USBC-65W", "USB-C Charger 65 W", "GaN charger for laptops and phones.", 39.90m),
            new("VE-CABLE-2M", "USB-C Cable 2 m", "Braided cable, 100 W power delivery.", 14.90m),
            new("VE-USBC-30W", "USB-C Charger 30 W", "Compact charger for phones and tablets.", 24.90m),
            new("VE-USBC-140W", "USB-C Charger 140 W", "Three ports, enough for a workstation laptop.", 79.90m),
            new("VE-CABLE-1M", "USB-C Cable 1 m", "Short braided cable for the desk.", 9.90m),
            new("VE-CABLE-LIGHTNING", "USB-C to Lightning Cable", "Certified cable for fast charging.", 19.90m),
        ]),
        new("Audio",
        [
            new("VE-BUDS-PRO", "Wireless Earbuds Pro", "Noise cancelling earbuds with a charging case.", 129.00m),
            new("VE-HEADPHONES-ANC", "Over-Ear Headphones ANC", "Forty hours on a charge.", 199.00m),
            new("VE-SPEAKER-MINI", "Portable Speaker Mini", "Pocket speaker, water resistant.", 59.90m),
        ]),
        new("Power",
        [
            new("VE-BANK-10K", "Power Bank 10 000 mAh", "Slim bank with a 30 W output.", 34.90m),
            new("VE-BANK-20K", "Power Bank 20 000 mAh", "Charges a laptop and a phone at once.", 54.90m),
            new("VE-HUB-7", "USB-C Hub 7-in-1", "HDMI, card reader and three USB ports.", 69.90m),
        ]),
    ];

    public static async Task SeedDevelopmentCatalogAsync(
        this IServiceProvider services,
        Guid tenantId,
        Guid woodenHomeStoreId,
        Guid voltElectronicsStoreId,
        CancellationToken cancellationToken = default)
    {
        await SeedStoreAsync(services, tenantId, woodenHomeStoreId, WoodenHome, cancellationToken);
        await SeedStoreAsync(services, tenantId, voltElectronicsStoreId, VoltElectronics, cancellationToken);

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
                ["oak-carving-board-xl"] = new() { ["material"] = new[] { "oak" }, ["length"] = 60m, ["dishwasher-safe"] = false },
                ["round-beech-board"] = new() { ["material"] = new[] { "beech" }, ["length"] = 32m, ["dishwasher-safe"] = true },
                ["walnut-cheese-board"] = new() { ["material"] = new[] { "walnut" }, ["length"] = 28m, ["dishwasher-safe"] = false },
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
                ["usb-c-charger-30-w"] = new() { ["power"] = 30L, ["connectors"] = new[] { "usb-c" } },
                ["usb-c-charger-140-w"] = new() { ["power"] = 140L, ["connectors"] = new[] { "usb-c", "usb-a" } },
                ["usb-c-cable-1-m"] = new() { ["power"] = 100L, ["connectors"] = new[] { "usb-c" }, ["cable-length"] = 1m },
                ["usb-c-to-lightning-cable"] = new() { ["power"] = 30L, ["connectors"] = new[] { "usb-c", "lightning" }, ["cable-length"] = 1.5m },
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

        var definitions = await dbContext.Set<AttributeDefinition>().Include(definition => definition.Options).ToListAsync(cancellationToken);

        if (definitions.Count == 0)
        {
            definitions = [.. attributes.Select((attribute, index) =>
            {
                var definition = new AttributeDefinition(storeId, attribute.Code, attribute.Name, attribute.Type,
                    new AttributeSettings(attribute.Unit, IsFilterable: true, IsVisibleOnProductPage: true, SortOrder: index));

                foreach (var option in attribute.Options)
                {
                    definition.AddOption(option, Slugs.Create(option));
                }

                return definition;
            })];
            dbContext.AddRange(definitions);

            var category = await dbContext.Set<Category>().Include(category => category.Attributes).SingleAsync(category => category.Slug == categorySlug, cancellationToken);
            category.AssignAttributes(definitions);
        }

        var storeProducts = await dbContext.Set<StoreProduct>()
            .Include(storeProduct => storeProduct.AttributeValues)
            .Where(storeProduct => valuesBySlug.Keys.Contains(storeProduct.Slug))
            .ToListAsync(cancellationToken);

        foreach (var storeProduct in storeProducts.Where(storeProduct => storeProduct.AttributeValues.Count == 0))
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
        SeedCategory[] categories,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(storeId, tenantId);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var currency = (await scope.ServiceProvider.GetRequiredService<ICurrentStoreSettings>().GetAsync(cancellationToken)).Currency;
        var added = new List<Product>();

        for (var index = 0; index < categories.Length; index++)
        {
            var (name, items) = categories[index];
            var slug = Slugs.Create(name);
            var category = await dbContext.Set<Category>().SingleOrDefaultAsync(candidate => candidate.Slug == slug, cancellationToken);

            if (category is null)
            {
                category = new Category(storeId, name, slug, sortOrder: index);
                dbContext.Add(category);
            }

            foreach (var item in items)
            {
                if (await dbContext.Set<ProductVariant>().AnyAsync(candidate => candidate.Sku == item.Sku, cancellationToken))
                {
                    continue;
                }

                var product = new Product(tenantId, item.Sku, ean: null, weightGrams: null);
                var storeProduct = new StoreProduct(storeId, product,
                    new StoreProductDetails(item.Name, Slugs.Create(item.Name), item.Description, item.Price, VatRate: 21m, IsVisible: true, SortOrder: 0),
                    currency);
                storeProduct.AssignCategories([category]);
                dbContext.AddRange(product, storeProduct);
                added.Add(product);
            }
        }

        if (added.Count == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var stock = scope.ServiceProvider.GetRequiredService<IStockLedger>();

        foreach (var product in added)
        {
            await stock.SetOnHandAsync(product.Default.Id, 25, "seed", cancellationToken);
        }
    }

    private sealed record SeedCategory(string Name, SeedItem[] Items);

    private sealed record SeedItem(string Sku, string Name, string Description, decimal Price);
}

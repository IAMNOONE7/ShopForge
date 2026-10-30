using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Import;
using ShopForge.Catalog.Search;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tax;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Development;

// The development data's Czech tool shop (`ToolShopCatalogue`, D-225). Its category tree and attributes are written
// here; its products arrive through the importer, as a merchant's file would bring them, so they are measured,
// stocked and searchable by the rules every import follows. A store that already sells something is left alone.
public static class DevelopmentToolShop
{
    private const int StockPerProduct = 25;

    public static async Task SeedDevelopmentToolShopAsync(
        this IServiceProvider services,
        Guid tenantId,
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var storeContext = scope.ServiceProvider.GetRequiredService<StoreContext>();
        storeContext.Set(storeId, tenantId);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        if (await dbContext.Set<StoreProduct>().AnyAsync(cancellationToken))
        {
            return;
        }

        var categories = new Dictionary<string, Category>();

        foreach (var (entry, index) in ToolShopCatalogue.Categories.Select((entry, index) => (entry, index)))
        {
            var category = new Category(storeId, entry.Name, entry.Slug, index);
            category.MoveTo(entry.ParentSlug is { } parent ? categories[parent].Id : null);
            category.SetSearchKeywords(entry.Keywords);
            categories[entry.Slug] = category;
        }

        var definitions = ToolShopCatalogue.Attributes.Select((attribute, index) =>
        {
            var definition = new AttributeDefinition(storeId, attribute.Code, attribute.Name, Enum.Parse<AttributeType>(attribute.Type, ignoreCase: true),
                new AttributeSettings(attribute.Unit, IsFilterable: attribute.Filterable, IsVisibleOnProductPage: true, SortOrder: index, IsSearchable: attribute.Searchable));

            foreach (var option in attribute.Options)
            {
                definition.AddOption(option, Slugs.Create(option));
            }

            return (attribute, definition);
        }).ToList();

        dbContext.AddRange(categories.Values);
        dbContext.AddRange(definitions.Select(pair => pair.definition));

        foreach (var (slug, category) in categories)
        {
            category.AssignAttributes([.. definitions.Where(pair => pair.attribute.FilterIn.Contains(slug)).Select(pair => pair.definition)]);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var provider = scope.ServiceProvider;
        var importer = new CatalogImporter(
            dbContext,
            storeContext,
            provider.GetRequiredService<IStockLedger>(),
            provider.GetRequiredService<ITenantLimits>(),
            provider.GetRequiredService<ITaxRates>(),
            provider.GetRequiredService<SearchIndex>(),
            provider.GetRequiredService<CategorySearchText>(),
            provider.GetRequiredService<EveryStoreSearch>(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<TimeProvider>(),
            (await provider.GetRequiredService<ICurrentStoreSettings>().GetAsync(cancellationToken)).Currency);
        var report = await importer.ImportAsync(ImportFile.Read(new MemoryStream(ProductsWorkbook())), cancellationToken);

        if (report.Created != ToolShopCatalogue.Products.Count)
        {
            throw new InvalidOperationException($"The tool shop's products did not all import: {report.Created} created, {report.Invalid} invalid, {report.Failed} failed.");
        }
    }

    // Every product as a merchant's file would bring it, one row a product in the import's own columns, each weighed and
    // boxed as a tool of its kind might be. The development data reads it in, the search's tests and a run of the
    // end-to-end rig upload it, so all three have the same products by the same rules (D-227).
    public static byte[] ProductsWorkbook()
    {
        var codes = ToolShopCatalogue.Attributes.Select(attribute => attribute.Code).ToArray();
        string[] columns =
        [
            "sku", "name", "slug", "description", "price", "vat", "stock", "visible", "sortorder", "categories", "brand", "ean", "partnumber",
            "search_keywords", "weight", "package_length_mm", "package_width_mm", "package_height_mm", .. codes,
        ];

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Products");

        for (var column = 0; column < columns.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = columns[column];
        }

        foreach (var (product, row) in ToolShopCatalogue.Products.Select((product, index) => (product, index + 2)))
        {
            object?[] values =
            [
                product.Sku, product.Name, product.Slug, product.Description, product.Price, 21m, StockPerProduct, product.Visible,
                product.SortOrder, product.Category, product.Brand, product.Ean, product.PartNumber, product.Keywords, 600, 350, 120, 60,
                .. codes.Select(code => product.Values.GetValueOrDefault(code)),
            ];

            for (var column = 0; column < values.Length; column++)
            {
                sheet.Cell(row, column + 1).Value = XLCellValue.FromObject(values[column]);
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }
}

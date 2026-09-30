using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Import;

// Everything the file can touch is loaded once up front, so importing a row needs no further queries.
internal sealed class CatalogData
{
    private CatalogData(
        Guid storeId,
        Dictionary<string, Product> products,
        Dictionary<Guid, StoreProduct> listings,
        Dictionary<string, Category> categories,
        Dictionary<string, AttributeDefinition> definitions,
        Dictionary<string, Guid> slugs)
    {
        StoreId = storeId;
        Products = products;
        Listings = listings;
        Categories = categories;
        Definitions = definitions;
        Slugs = slugs;
    }

    public Guid StoreId { get; }

    public Dictionary<string, Product> Products { get; }

    public Dictionary<Guid, StoreProduct> Listings { get; }

    public Dictionary<string, Category> Categories { get; }

    public Dictionary<string, AttributeDefinition> Definitions { get; }

    public Dictionary<string, Guid> Slugs { get; }

    public HashSet<string> SeenSkus { get; } = new(StringComparer.Ordinal);

    public static async Task<CatalogData> LoadAsync(DbContext dbContext, Guid storeId, ImportFile file, CancellationToken cancellationToken)
    {
        var skus = file.Rows
            .Select(row => row[ImportColumns.Sku].Text.ToUpperInvariant())
            .Where(sku => sku.Length > 0)
            .Distinct()
            .ToList();

        var products = await dbContext.Set<Product>()
            .Where(product => product.Variants.Any(variant => skus.Contains(variant.Sku)))
            .ToListAsync(cancellationToken);
        var productIds = products.Select(product => product.Id).ToList();

        var listings = await dbContext.Set<StoreProduct>()
            .Include(listing => listing.Categories)
            .Include(listing => listing.AttributeValues)
            .Where(listing => productIds.Contains(listing.ProductId))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var categories = await dbContext.Set<Category>().ToListAsync(cancellationToken);
        var definitions = await dbContext.Set<AttributeDefinition>()
            .Include(definition => definition.Options)
            .Where(definition => file.Columns.Contains(definition.Code))
            .ToListAsync(cancellationToken);
        var slugs = await dbContext.Set<StoreProduct>().Select(listing => new { listing.Slug, listing.Id }).ToListAsync(cancellationToken);

        return new CatalogData(
            storeId,
            products.ToDictionary(product => product.Default.Sku, StringComparer.Ordinal),
            listings.ToDictionary(listing => listing.ProductId),
            categories.ToDictionary(category => category.Slug, StringComparer.Ordinal),
            definitions.ToDictionary(definition => definition.Code, StringComparer.Ordinal),
            slugs.ToDictionary(listing => listing.Slug, listing => listing.Id, StringComparer.Ordinal));
    }
}

internal static class ImportColumns
{
    public const string Sku = "sku";
    public const string Name = "name";
    public const string Slug = "slug";
    public const string Description = "description";
    public const string Price = "price";
    public const string Stock = "stock";
    public const string Vat = "vat";
    public const string Visible = "visible";
    public const string SortOrder = "sortorder";
    public const string Categories = "categories";
    public const string Ean = "ean";
    public const string Weight = "weight";

    public static readonly string[] All = [Sku, Name, Slug, Description, Price, Vat, Stock, Visible, SortOrder, Categories, Ean, Weight];
}

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
        Dictionary<string, StoreProduct> listingsBySlug,
        Dictionary<string, Category> categories,
        Dictionary<string, AttributeDefinition> definitions,
        Dictionary<string, Guid> slugs)
    {
        StoreId = storeId;
        Products = products;
        Listings = listings;
        ListingsBySlug = listingsBySlug;
        Categories = categories;
        Definitions = definitions;
        Slugs = slugs;
    }

    public Guid StoreId { get; }

    public Dictionary<string, Product> Products { get; }

    public Dictionary<Guid, StoreProduct> Listings { get; }

    // A row for a SKU nobody has seen may still belong to a product the store already lists: that is how a
    // second size is added to a shirt that is already on sale (D-137).
    public Dictionary<string, StoreProduct> ListingsBySlug { get; }

    public Dictionary<string, Category> Categories { get; }

    public Dictionary<string, AttributeDefinition> Definitions { get; }

    public Dictionary<string, Guid> Slugs { get; }

    public HashSet<string> SeenSkus { get; } = new(StringComparer.Ordinal);

    public Dictionary<Guid, Product> ProductsById { get; } = [];

    public Product ProductOf(StoreProduct listing) => ProductsById[listing.ProductId];

    public static async Task<CatalogData> LoadAsync(DbContext dbContext, Guid storeId, ImportFile file, CancellationToken cancellationToken)
    {
        var skus = file.Rows
            .Select(row => row[ImportColumns.Sku].Text.ToUpperInvariant())
            .Where(sku => sku.Length > 0)
            .Distinct()
            .ToList();

        var fileSlugs = file.Rows
            .Select(row => row.Has(ImportColumns.Slug) ? row[ImportColumns.Slug].Text : Domain.Slugs.Create(row[ImportColumns.Name].Text))
            .Where(slug => slug.Length > 0)
            .Distinct()
            .ToList();

        var listedProductIds = await dbContext.Set<StoreProduct>()
            .Where(listing => fileSlugs.Contains(listing.Slug))
            .Select(listing => listing.ProductId)
            .ToListAsync(cancellationToken);

        var products = await dbContext.Set<Product>()
            .Where(product => product.Variants.Any(variant => skus.Contains(variant.Sku)) || listedProductIds.Contains(product.Id))
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

        var catalog = new CatalogData(
            storeId,
            products
                .SelectMany(product => product.Variants.Select(variant => (variant.Sku, Product: product)))
                .ToDictionary(entry => entry.Sku, entry => entry.Product, StringComparer.Ordinal),
            listings.ToDictionary(listing => listing.ProductId),
            listings.ToDictionary(listing => listing.Slug, StringComparer.Ordinal),
            categories.ToDictionary(category => category.Slug, StringComparer.Ordinal),
            definitions.ToDictionary(definition => definition.Code, StringComparer.Ordinal),
            slugs.ToDictionary(listing => listing.Slug, listing => listing.Id, StringComparer.Ordinal));

        foreach (var product in products)
        {
            catalog.ProductsById[product.Id] = product;
        }

        return catalog;
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

    // An axis is named by the column header after this prefix, and the cell holds that row's value for it:
    // "option:size" holding "L". Several of them make several axes, in the order the columns appear (D-137).
    public const string OptionPrefix = "option:";

    public static readonly string[] All = [Sku, Name, Slug, Description, Price, Vat, Stock, Visible, SortOrder, Categories, Ean, Weight];

    public static bool IsOption(string column) => column.StartsWith(OptionPrefix, StringComparison.Ordinal);

    // "option:size" names the axis "Size": the file is read in lower case, and an axis is read by a shopper.
    public static string AxisOf(string column)
    {
        var name = column[OptionPrefix.Length..].Trim();

        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }
}

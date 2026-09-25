using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Import;

internal sealed class CatalogImporter(DbContext dbContext, IStoreContext storeContext, IStockLedger stock, ITenantLimits limits)
{
    private const int MaxIssues = 200;

    private readonly List<StockUpdate> _stockUpdates = [];

    public async Task<ImportReport> ImportAsync(ImportFile file, CancellationToken cancellationToken)
    {
        if (!file.Columns.Contains(ImportColumns.Sku))
        {
            throw new ImportFileException($"A '{ImportColumns.Sku}' column is required.");
        }

        var catalog = await CatalogData.LoadAsync(dbContext, storeContext.StoreId!.Value, file, cancellationToken);
        var issues = new List<ImportIssue>();
        var outcomes = new List<RowOutcome>();

        foreach (var row in file.Rows)
        {
            var rowIssues = new List<ImportIssue>();
            var outcome = Apply(row, catalog, rowIssues);

            outcomes.Add(outcome);
            issues.AddRange(rowIssues);
        }

        var failed = 0;

        // A file that would take the company past what its plan covers is not imported at all: half a catalog is
        // worse than a clear refusal (D-108).
        if (await Plans.RefusedAsync(dbContext, limits, NewProducts(), cancellationToken) is not null)
        {
            var wouldHaveChanged = outcomes.Count(outcome => outcome is RowOutcome.Created or RowOutcome.Updated);
            outcomes.RemoveAll(outcome => outcome is RowOutcome.Created or RowOutcome.Updated);
            issues.Add(new ImportIssue(0, null, $"Nothing was imported: the plan does not cover {NewProducts()} more products."));

            return Report(file, catalog, outcomes, wouldHaveChanged, issues);
        }

        if (outcomes.Any(outcome => outcome is RowOutcome.Created or RowOutcome.Updated))
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await ApplyStockAsync(issues, cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                failed = outcomes.Count(outcome => outcome is RowOutcome.Created or RowOutcome.Updated);
                outcomes.RemoveAll(outcome => outcome is RowOutcome.Created or RowOutcome.Updated);
                issues.Add(new ImportIssue(0, null, $"Nothing was imported: {exception.InnerException?.Message ?? exception.Message}"));
            }
        }

        return Report(file, catalog, outcomes, failed, issues);
    }

    private int NewProducts() => dbContext.ChangeTracker.Entries<Product>().Count(entry => entry.State == EntityState.Added);

    private static ImportReport Report(ImportFile file, CatalogData catalog, List<RowOutcome> outcomes, int failed, List<ImportIssue> issues) =>
        new(
            outcomes.Count(outcome => outcome == RowOutcome.Created),
            outcomes.Count(outcome => outcome == RowOutcome.Updated),
            outcomes.Count(outcome => outcome == RowOutcome.Unchanged),
            outcomes.Count(outcome => outcome == RowOutcome.Invalid),
            failed,
            [.. file.Columns.Where(column => !ImportColumns.All.Contains(column) && !catalog.Definitions.ContainsKey(column))],
            [.. issues.Take(MaxIssues)]);

    // Products only have their ids once the catalog is saved, so stock is written afterwards, from the rows that were valid.
    private async Task ApplyStockAsync(List<ImportIssue> issues, CancellationToken cancellationToken)
    {
        foreach (var update in _stockUpdates)
        {
            if (!await stock.SetOnHandAsync(update.Product.Id, update.Quantity, "import", cancellationToken))
            {
                issues.Add(new ImportIssue(update.Row, ImportColumns.Stock, "Stock was left unchanged: open orders reserve more items than this."));
            }
        }
    }

    private RowOutcome Apply(ImportRow row, CatalogData catalog, List<ImportIssue> issues)
    {
        void Invalid(string? column, string message) => issues.Add(new ImportIssue(row.Number, column, message));

        var sku = row[ImportColumns.Sku].Text.ToUpperInvariant();

        if (sku.Length == 0)
        {
            Invalid(ImportColumns.Sku, "SKU is required.");
            return RowOutcome.Invalid;
        }

        if (!catalog.SeenSkus.Add(sku))
        {
            Invalid(ImportColumns.Sku, "The same SKU appears more than once in the file.");
            return RowOutcome.Invalid;
        }

        var product = catalog.Products.GetValueOrDefault(sku);
        var listing = product is null ? null : catalog.Listings.GetValueOrDefault(product.Id);
        var details = ReadDetails(row, listing, catalog, issues);
        var categoryNames = ReadCategoryNames(row, issues);
        var attributes = ReadAttributes(row, catalog, issues);
        var ean = ReadEan(row, product, issues);
        var weight = ReadWeight(row, product, issues);
        var stockQuantity = ReadStock(row, issues);

        if (issues.Count > 0)
        {
            return RowOutcome.Invalid;
        }

        var changed = false;

        if (product is null)
        {
            product = new Product(storeContext.TenantId!.Value, sku, ean, weight);
            dbContext.Add(product);
            catalog.Products[sku] = product;
            changed = true;
        }
        else
        {
            changed |= product.UpdatePhysicalData(ean, weight);
        }

        var created = listing is null;

        if (listing is null)
        {
            listing = new StoreProduct(storeContext.StoreId!.Value, product, details!);
            dbContext.Add(listing);
            catalog.Listings[product.Id] = listing;
        }
        else
        {
            changed |= listing.Update(details!);
        }

        catalog.Slugs[details!.Slug] = listing.Id;
        changed |= listing.AddToCategories(ResolveCategories(categoryNames, catalog));

        foreach (var (definition, pending) in attributes)
        {
            changed |= listing.SetAttributeValue(definition, AttributeCells.Resolve(definition, pending));
        }

        if (stockQuantity is { } quantity)
        {
            _stockUpdates.Add(new StockUpdate(row.Number, product, quantity));
            changed = true;
        }

        return created ? RowOutcome.Created : changed ? RowOutcome.Updated : RowOutcome.Unchanged;
    }

    private static int? ReadStock(ImportRow row, List<ImportIssue> issues)
    {
        if (!row.Has(ImportColumns.Stock))
        {
            return null;
        }

        if (row[ImportColumns.Stock].TryInteger(out var quantity) && quantity >= 0)
        {
            return (int)quantity;
        }

        issues.Add(new ImportIssue(row.Number, ImportColumns.Stock, "Stock must be a whole number of zero or more."));

        return null;
    }

    private static StoreProductDetails? ReadDetails(ImportRow row, StoreProduct? listing, CatalogData catalog, List<ImportIssue> issues)
    {
        void Invalid(string? column, string message) => issues.Add(new ImportIssue(row.Number, column, message));

        var name = row.Has(ImportColumns.Name) ? row[ImportColumns.Name].Text : listing?.Name;
        name = string.IsNullOrWhiteSpace(name) ? null : name;
        var description = row.Has(ImportColumns.Description) ? row[ImportColumns.Description].Text : listing?.Description;
        decimal? price = listing?.Price;
        decimal? vatRate = listing?.VatRate;
        var visible = listing?.IsVisible ?? true;
        var sortOrder = listing?.SortOrder ?? 0;

        if (string.IsNullOrWhiteSpace(name))
        {
            Invalid(ImportColumns.Name, "Name is required for a product that is not yet listed in this store.");
        }

        if (row.Has(ImportColumns.Price))
        {
            if (row[ImportColumns.Price].TryDecimal(out var value) && value >= 0 && decimal.Round(value, 2) == value)
            {
                price = value;
            }
            else
            {
                Invalid(ImportColumns.Price, "Price must be zero or more, with at most two decimals.");
            }
        }
        else if (price is null)
        {
            Invalid(ImportColumns.Price, "Price is required for a product that is not yet listed in this store.");
        }

        if (row.Has(ImportColumns.Vat))
        {
            if (row[ImportColumns.Vat].TryDecimal(out var value) && value is >= 0 and <= 100 && decimal.Round(value, 2) == value)
            {
                vatRate = value;
            }
            else
            {
                Invalid(ImportColumns.Vat, "The VAT rate must be between 0 and 100.");
            }
        }
        else if (vatRate is null)
        {
            Invalid(ImportColumns.Vat, "A VAT rate is required for a product that is not yet listed in this store.");
        }

        if (row.Has(ImportColumns.Visible) && !row[ImportColumns.Visible].TryBoolean(out visible))
        {
            Invalid(ImportColumns.Visible, "Use true or false.");
        }

        if (row.Has(ImportColumns.SortOrder))
        {
            if (row[ImportColumns.SortOrder].TryInteger(out var value) && value is >= int.MinValue and <= int.MaxValue)
            {
                sortOrder = (int)value;
            }
            else
            {
                Invalid(ImportColumns.SortOrder, "Sort order must be a whole number.");
            }
        }

        // With no name there is nothing to derive a slug from; the missing name is the only useful message.
        var slug = row.Has(ImportColumns.Slug) ? row[ImportColumns.Slug].Text : listing?.Slug ?? (name is null ? null : Slugs.Create(name));

        if (slug is not null && !Slugs.IsValid(slug))
        {
            Invalid(ImportColumns.Slug, "Slug may contain lower-case letters, digits and single hyphens.");
        }
        else if (slug is not null && catalog.Slugs.TryGetValue(slug, out var owner) && owner != listing?.Id)
        {
            Invalid(ImportColumns.Slug, $"The slug '{slug}' is already used by another product in this store.");
        }

        return issues.Count > 0 ? null : new StoreProductDetails(name!, slug!, description, price!.Value, vatRate!.Value, visible, sortOrder);
    }

    private static List<string> ReadCategoryNames(ImportRow row, List<ImportIssue> issues)
    {
        var names = Split(row[ImportColumns.Categories].Text).ToList();

        foreach (var name in names.Where(name => Slugs.Create(name).Length == 0))
        {
            issues.Add(new ImportIssue(row.Number, ImportColumns.Categories, $"'{name}' is not a usable category name."));
        }

        return names;
    }

    private List<Category> ResolveCategories(IEnumerable<string> names, CatalogData catalog)
    {
        var categories = new List<Category>();

        foreach (var name in names)
        {
            var slug = Slugs.Create(name);

            if (!catalog.Categories.TryGetValue(slug, out var category))
            {
                category = new Category(catalog.StoreId, name, slug, catalog.Categories.Count);
                catalog.Categories[slug] = category;
                dbContext.Add(category);
            }

            categories.Add(category);
        }

        return categories;
    }

    private static List<(AttributeDefinition Definition, PendingAttributeValue Value)> ReadAttributes(ImportRow row, CatalogData catalog, List<ImportIssue> issues)
    {
        var values = new List<(AttributeDefinition, PendingAttributeValue)>();

        foreach (var (column, definition) in catalog.Definitions.Where(entry => row.Has(entry.Key)))
        {
            if (AttributeCells.TryRead(definition, row[column], out var value))
            {
                values.Add((definition, value));
            }
            else
            {
                issues.Add(new ImportIssue(row.Number, column, AttributeCells.Expected(definition)));
            }
        }

        return values;
    }

    private static string? ReadEan(ImportRow row, Product? product, List<ImportIssue> issues)
    {
        if (!row.Has(ImportColumns.Ean))
        {
            return product?.Ean;
        }

        var ean = row[ImportColumns.Ean].Text;

        if (ean.Length is >= 8 and <= 14 && ean.All(char.IsAsciiDigit))
        {
            return ean;
        }

        issues.Add(new ImportIssue(row.Number, ImportColumns.Ean, "EAN must have 8 to 14 digits."));
        return null;
    }

    private static int? ReadWeight(ImportRow row, Product? product, List<ImportIssue> issues)
    {
        if (!row.Has(ImportColumns.Weight))
        {
            return product?.WeightGrams;
        }

        if (row[ImportColumns.Weight].TryInteger(out var weight) && weight is >= 0 and <= int.MaxValue)
        {
            return (int)weight;
        }

        issues.Add(new ImportIssue(row.Number, ImportColumns.Weight, "Weight must be a whole number of grams."));
        return null;
    }

    internal static IEnumerable<string> Split(string text) =>
        text.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal enum RowOutcome
{
    Created,
    Updated,
    Unchanged,
    Invalid,
}

internal sealed record StockUpdate(int Row, Product Product, int Quantity);

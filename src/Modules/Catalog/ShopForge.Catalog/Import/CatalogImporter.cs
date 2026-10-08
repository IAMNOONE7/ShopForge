using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Publishing;
using ShopForge.Shared.Inventory;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Platform;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Import;

internal sealed class CatalogImporter(DbContext dbContext, IStoreContext storeContext, IStockLedger stock, ITenantLimits limits, TimeProvider clock, Currency currency)
{
    private const int MaxIssues = 200;

    private readonly List<StockUpdate> _stockUpdates = [];
    private readonly List<Rename> _renames = [];

    private string[] _axes = [];

    private string[] _optionColumns = [];

    public async Task<ImportReport> ImportAsync(ImportFile file, CancellationToken cancellationToken)
    {
        if (!file.Columns.Contains(ImportColumns.Sku))
        {
            throw new ImportFileException($"A '{ImportColumns.Sku}' column is required.");
        }

        _optionColumns = [.. file.Columns.Where(ImportColumns.IsOption)];
        _axes = [.. _optionColumns.Select(ImportColumns.AxisOf)];

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
                // The trail goes in the same save as the rename it records: either both or neither (D-166).
                await RecordRenamesAsync(cancellationToken);
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
            [.. file.Columns.Where(column =>
                !ImportColumns.All.Contains(column) && !ImportColumns.IsOption(column) && !catalog.Definitions.ContainsKey(column))],
            [.. issues.Take(MaxIssues)]);

    private async Task RecordRenamesAsync(CancellationToken cancellationToken)
    {
        foreach (var rename in _renames)
        {
            await SlugTrail.RecordAsync(
                dbContext,
                rename.StoreId,
                SlugKind.Listing,
                rename.WasCalled,
                rename.Listing.Slug,
                rename.Listing.Id,
                clock.GetUtcNow(),
                cancellationToken);
        }
    }

    // Products only have their ids once the catalog is saved, so stock is written afterwards, from the rows that were valid.
    private async Task ApplyStockAsync(List<ImportIssue> issues, CancellationToken cancellationToken)
    {
        foreach (var update in _stockUpdates)
        {
            if (!await stock.SetOnHandAsync(update.Variant.Id, update.Quantity, "import", cancellationToken))
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

        // A SKU nobody has seen may still be another form of a product the store already sells: the rows are
        // tied together by the listing they share, which is the slug (D-137).
        if (product is null && SlugOf(row) is { Length: > 0 } slug && catalog.ListingsBySlug.GetValueOrDefault(slug) is { } listed)
        {
            listing = listed;
            product = catalog.ProductOf(listed);
        }

        var details = ReadDetails(row, listing, catalog, issues, currency);
        var categoryNames = ReadCategoryNames(row, issues);
        var attributes = ReadAttributes(row, catalog, issues);
        var variant = product?.Variants.SingleOrDefault(candidate => candidate.Sku == sku);
        var ean = ReadEan(row, variant, issues);
        var weight = ReadWeight(row, variant, issues);
        var brand = ReadBrand(row, product, issues);
        var partNumber = ReadPartNumber(row, variant, issues);
        var condition = ReadCondition(row, variant, issues);
        var stockQuantity = ReadStock(row, issues);
        var optionValues = ReadOptions(row, product, issues);

        if (issues.Count > 0)
        {
            return RowOutcome.Invalid;
        }

        var changed = false;
        var newForm = variant is null;

        if (product is null)
        {
            product = new Product(storeContext.TenantId!.Value, sku, ean, weight);
            dbContext.Add(product);
            variant = product.Default;
            changed = true;
        }
        else if (variant is null)
        {
            variant = product.AddVariant(sku, ean, weight, optionValues);
            changed = true;
        }

        // The identifiers a feed asks for, set the same way however the row arrived: a new product, a new
        // form of one already here, or a row that changes what was loaded before (D-163).
        changed |= product.Rebrand(brand);
        changed |= variant.UpdatePhysicalData(ean, weight, partNumber, condition);

        catalog.Products[sku] = product;
        catalog.ProductsById[product.Id] = product;

        if (_axes.Length > 0)
        {
            changed |= product.SellAlong(_axes, variant, optionValues);
        }

        // A row brought something new into the catalog if it added a listing or a form of one; a row that only
        // restates what is already there is an update.
        var created = listing is null || newForm;

        if (listing is null)
        {
            listing = new StoreProduct(storeContext.StoreId!.Value, product, details!, currency);
            dbContext.Add(listing);
            catalog.Listings[product.Id] = listing;
        }
        else
        {
            // A file can rename in bulk, which is the easiest way to lose every link to a shop at once. The
            // trail is written after the rows, where the rest of the deferred work goes.
            var wasCalled = listing.Slug;
            changed |= listing.Update(details!, currency);

            if (wasCalled != listing.Slug)
            {
                _renames.Add(new Rename(listing.StoreId, wasCalled, listing));
            }
        }

        catalog.Slugs[details!.Slug] = listing.Id;
        changed |= listing.AddToCategories(ResolveCategories(categoryNames, catalog));

        foreach (var (definition, pending) in attributes)
        {
            changed |= listing.SetAttributeValue(definition, AttributeCells.Resolve(definition, pending));
        }

        catalog.ListingsBySlug[details.Slug] = listing;

        if (stockQuantity is { } quantity)
        {
            _stockUpdates.Add(new StockUpdate(row.Number, variant, quantity));
            changed = true;
        }

        return created ? RowOutcome.Created : changed ? RowOutcome.Updated : RowOutcome.Unchanged;
    }

    // What this row says about the form it describes: one value per axis the file names, in column order.
    private string[] ReadOptions(ImportRow row, Product? product, List<ImportIssue> issues)
    {
        void Invalid(string? column, string message) => issues.Add(new ImportIssue(row.Number, column, message));

        var values = new string[_optionColumns.Length];

        for (var axis = 0; axis < _optionColumns.Length; axis++)
        {
            values[axis] = row[_optionColumns[axis]].Text.Trim();

            if (values[axis].Length == 0)
            {
                Invalid(_optionColumns[axis], $"A value for {_axes[axis]} is needed, because the file sells this product along it.");
            }
        }

        if (product is null || product.OptionNames.SequenceEqual(_axes, StringComparer.Ordinal))
        {
            return values;
        }

        // Naming new axes for a product that is already sold in several forms would leave the forms the file
        // does not mention with nothing to say for themselves.
        if (product.OptionNames.Length > 0)
        {
            Invalid(null, $"This product is already sold along {string.Join(", ", product.OptionNames)}; the file names {Named(_axes)}.");
        }
        else if (product.Variants.Count > 1)
        {
            Invalid(null, "This product is already sold in several forms with no axes; name the axes in the admin first.");
        }

        return values;
    }

    private static string Named(string[] axes) => axes.Length == 0 ? "none" : string.Join(", ", axes);

    private static string SlugOf(ImportRow row) =>
        row.Has(ImportColumns.Slug) ? row[ImportColumns.Slug].Text : Slugs.Create(row[ImportColumns.Name].Text);

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

    private static StoreProductDetails? ReadDetails(ImportRow row, StoreProduct? listing, CatalogData catalog, List<ImportIssue> issues, Currency currency)
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
            if (row[ImportColumns.Price].TryDecimal(out var value) && value >= 0 && currency.Holds(value))
            {
                price = value;
            }
            else
            {
                Invalid(ImportColumns.Price, $"Price must be zero or more, with at most {currency.Decimals} decimals.");
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

    private static string? ReadEan(ImportRow row, ProductVariant? variant, List<ImportIssue> issues)
    {
        if (!row.Has(ImportColumns.Ean))
        {
            return variant?.Ean;
        }

        var ean = row[ImportColumns.Ean].Text;

        if (ean.Length == 0 || Gtin.IsValid(ean))
        {
            return ean.Length == 0 ? null : ean;
        }

        // A feed rejects the whole product for a barcode that does not check out, so the import refuses the
        // row rather than loading one (D-163).
        issues.Add(new ImportIssue(row.Number, ImportColumns.Ean, "A barcode must be a GTIN of 8, 12, 13 or 14 digits whose check digit agrees."));
        return null;
    }

    // The brand is the product's, so every row of one product says the same thing and the last one wins.
    private static string? ReadBrand(ImportRow row, Product? product, List<ImportIssue> issues)
    {
        if (!row.Has(ImportColumns.Brand))
        {
            return product?.Brand;
        }

        var brand = row[ImportColumns.Brand].Text;

        if (brand.Length <= Product.MaxBrandLength)
        {
            return brand.Length == 0 ? null : brand;
        }

        issues.Add(new ImportIssue(row.Number, ImportColumns.Brand, $"A brand can be up to {Product.MaxBrandLength} characters."));
        return null;
    }

    private static string? ReadPartNumber(ImportRow row, ProductVariant? variant, List<ImportIssue> issues)
    {
        if (!row.Has(ImportColumns.PartNumber))
        {
            return variant?.PartNumber;
        }

        var partNumber = row[ImportColumns.PartNumber].Text;

        if (partNumber.Length <= ProductVariant.MaxPartNumberLength)
        {
            return partNumber.Length == 0 ? null : partNumber;
        }

        issues.Add(new ImportIssue(row.Number, ImportColumns.PartNumber, $"A part number can be up to {ProductVariant.MaxPartNumberLength} characters."));
        return null;
    }

    private static ProductCondition? ReadCondition(ImportRow row, ProductVariant? variant, List<ImportIssue> issues)
    {
        if (!row.Has(ImportColumns.Condition))
        {
            return variant?.Condition;
        }

        var condition = row[ImportColumns.Condition].Text;

        if (condition.Length == 0)
        {
            return null;
        }

        if (Enum.TryParse<ProductCondition>(condition, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        issues.Add(new ImportIssue(row.Number, ImportColumns.Condition, "A condition is New, Refurbished or Used."));
        return null;
    }

    private static int? ReadWeight(ImportRow row, ProductVariant? variant, List<ImportIssue> issues)
    {
        if (!row.Has(ImportColumns.Weight))
        {
            return variant?.WeightGrams;
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

internal sealed record StockUpdate(int Row, ProductVariant Variant, int Quantity);

// A listing whose slug changed during this file, kept until the save that makes the change real.
internal sealed record Rename(Guid StoreId, string WasCalled, StoreProduct Listing);

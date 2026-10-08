using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Attributes;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Import;

namespace ShopForge.Catalog.Export;

// The catalogue as a spreadsheet, in the columns the importer reads. An export a merchant edits and sends
// back has to be a file the importer accepts, so the headers are the importer's own vocabulary and the row
// shape is the one it expects: one row per form of a thing, because that is what a code names (D-185).
internal static class CatalogExport
{
    // A whole catalogue is built in memory before it is sent, so there is a number past which this stops
    // being a download and becomes an outage. Beyond it a merchant wants the database, not a spreadsheet.
    public const int MostRows = 20_000;

    public static async Task<MemoryStream> WriteAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var definitions = await dbContext.Set<AttributeDefinition>()
            .AsNoTracking()
            .Include(definition => definition.Options)
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.Name)
            .ToListAsync(cancellationToken);

        var categories = await dbContext.Set<Category>()
            .AsNoTracking()
            .ToDictionaryAsync(category => category.Id, category => category.Slug, cancellationToken);

        var listings = await dbContext.Set<StoreProduct>()
            .AsNoTracking()
            .Include(listing => listing.Categories)
            .Include(listing => listing.AttributeValues)
            .OrderBy(listing => listing.SortOrder)
            .ThenBy(listing => listing.Name)
            .Take(MostRows)
            .ToListAsync(cancellationToken);

        var productIds = listings.Select(listing => listing.ProductId).ToList();
        var products = await dbContext.Set<Product>()
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        // The axes, but only when every exported thing is sold along the same ones. The importer reads the
        // axes from the file's columns and applies them to every product in it (D-137), so a file carrying
        // the union of a mixed catalogue's axes would tell it that a chair is sold by colour and then refuse
        // the chair for having no colour — which is what a live round trip did before this line existed.
        //
        // With mixed axes the columns are left out, and an import of that file leaves every product's axes
        // exactly as they are. A merchant changes those in the admin; what this file is for is everything
        // else (D-185).
        var sold = products.Values.Select(product => product.OptionNames).ToList();
        var axes = sold.Count > 0 && sold.All(names => names.SequenceEqual(sold[0], StringComparer.OrdinalIgnoreCase))
            ? sold[0].ToList()
            : [];

        return Write(listings, products, categories, definitions, axes);
    }

    private static MemoryStream Write(
        List<StoreProduct> listings,
        Dictionary<Guid, Product> products,
        Dictionary<Guid, string> categories,
        List<AttributeDefinition> definitions,
        List<string> axes)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Products");
        var headers = ImportColumns.All
            .Concat(definitions.Select(definition => definition.Code))
            .Concat(axes.Select(axis => ImportColumns.OptionPrefix + axis.ToLowerInvariant()))
            .ToList();

        for (var column = 0; column < headers.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        var row = 2;

        foreach (var listing in listings)
        {
            if (!products.TryGetValue(listing.ProductId, out var product))
            {
                continue;
            }

            // One row per form, because the code a row is keyed by belongs to the form rather than to the
            // listing. A thing sold one way is one row, as it always was.
            foreach (var variant in product.Variants.OrderBy(variant => variant.Position))
            {
                Fill(sheet, row++, listing, product, variant, categories, definitions, axes);
            }
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return stream;
    }

    private static void Fill(
        IXLWorksheet sheet,
        int row,
        StoreProduct listing,
        Product product,
        ProductVariant variant,
        Dictionary<Guid, string> categories,
        List<AttributeDefinition> definitions,
        List<string> axes)
    {
        var column = 1;

        // A date goes in as a date, not as whatever the machine's culture calls one. The importer takes a
        // date cell or `yyyy-MM-dd` and nothing else, and an export its own importer refuses is not an
        // export — which is what the first version of this did (D-185).
        void Put(object? value)
        {
            var cell = sheet.Cell(row, column++);

            switch (value)
            {
                case null:
                    cell.Value = Blank.Value;
                    break;
                case string text:
                    cell.Value = text;
                    break;
                case bool flag:
                    cell.Value = flag;
                    break;
                case decimal number:
                    cell.Value = number;
                    break;
                case int whole:
                    cell.Value = whole;
                    break;
                case DateOnly date:
                    cell.Value = date.ToDateTime(TimeOnly.MinValue);
                    cell.Style.DateFormat.Format = "yyyy-MM-dd";
                    break;
                default:
                    cell.Value = value.ToString();
                    break;
            }
        }

        Put(variant.Sku);
        Put(listing.Name);
        Put(listing.Slug);
        Put(listing.Description);
        Put(listing.Price);
        Put(listing.VatRate);

        // Stock is the warehouse's answer rather than the catalogue's, and the importer treats an empty cell
        // as "leave it alone" — which is what an export that does not carry it should mean (D-185).
        Put(null);
        Put(listing.IsVisible);
        Put(listing.SortOrder);
        Put(string.Join(", ", listing.Categories
            .Select(assignment => categories.GetValueOrDefault(assignment.CategoryId))
            .Where(slug => slug is not null)));
        Put(variant.Ean);
        Put(variant.WeightGrams);
        Put(product.Brand);
        Put(variant.PartNumber);
        Put(variant.Condition?.ToString());

        foreach (var definition in definitions)
        {
            var written = AttributeValueJson.Write(
                definition, [.. listing.AttributeValues.Where(value => value.AttributeDefinitionId == definition.Id)], optionNames: false);

            Put(written is IEnumerable<string> many ? string.Join(", ", many) : written);
        }

        for (var axis = 0; axis < axes.Count; axis++)
        {
            var named = product.OptionNames
                .Select((name, index) => (name, index))
                .FirstOrDefault(pair => string.Equals(pair.name, axes[axis], StringComparison.OrdinalIgnoreCase), (null!, -1));

            Put(named.index >= 0 && named.index < variant.OptionValues.Length ? variant.OptionValues[named.index] : null);
        }
    }
}

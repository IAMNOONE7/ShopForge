using System.Globalization;
using ClosedXML.Excel;

namespace ShopForge.Catalog.Import;

internal sealed record ImportFile(IReadOnlyList<string> Columns, IReadOnlyList<ImportRow> Rows)
{
    public const int MaxRows = 5_000;
    public const long MaxBytes = 10 * 1024 * 1024;

    // Reads the first worksheet: the first row holds column names, every following row is a product.
    public static ImportFile Read(Stream stream)
    {
        using var workbook = OpenWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new ImportFileException("The file has no worksheet.");
        var used = sheet.RangeUsed() ?? throw new ImportFileException("The worksheet is empty.");

        var headerRow = used.FirstRow();
        var columns = headerRow.Cells().Select(cell => cell.GetString().Trim().ToLowerInvariant()).ToList();

        if (columns.Count == 0 || columns.All(string.IsNullOrEmpty))
        {
            throw new ImportFileException("The first row must contain column names.");
        }

        if (columns.Where(column => column.Length > 0).GroupBy(column => column).Any(group => group.Count() > 1))
        {
            throw new ImportFileException("Column names must be unique.");
        }

        var rows = new List<ImportRow>();

        foreach (var row in used.Rows().Skip(1))
        {
            if (rows.Count == MaxRows)
            {
                throw new ImportFileException($"The file has more than {MaxRows} rows.");
            }

            var cells = columns
                .Select((column, index) => (Column: column, Cell: new ImportCell(row.Cell(index + 1).Value)))
                .Where(entry => entry.Column.Length > 0)
                .ToDictionary(entry => entry.Column, entry => entry.Cell);

            if (cells.Values.Any(cell => !cell.IsEmpty))
            {
                rows.Add(new ImportRow(row.RowNumber(), cells));
            }
        }

        return new ImportFile([.. columns.Where(column => column.Length > 0)], rows);
    }

    private static XLWorkbook OpenWorkbook(Stream stream)
    {
        try
        {
            return new XLWorkbook(stream);
        }
        catch (Exception exception) when (exception is not ImportFileException)
        {
            throw new ImportFileException("The file could not be read as an Excel workbook (.xlsx).");
        }
    }
}

internal sealed record ImportRow(int Number, IReadOnlyDictionary<string, ImportCell> Cells)
{
    public ImportCell this[string column] => Cells.TryGetValue(column, out var cell) ? cell : default;

    public bool Has(string column) => !this[column].IsEmpty;
}

internal readonly record struct ImportCell(XLCellValue Value)
{
    public bool IsEmpty => Text.Length == 0;

    // Values are taken from the cell's own type, so numbers and dates do not depend on the file's locale.
    public string Text => Value switch
    {
        { IsText: true } => Value.GetText().Trim(),
        { IsNumber: true } => Value.GetNumber().ToString(CultureInfo.InvariantCulture),
        { IsBoolean: true } => Value.GetBoolean() ? "true" : "false",
        { IsDateTime: true } => DateOnly.FromDateTime(Value.GetDateTime()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    public bool TryDecimal(out decimal number)
    {
        if (Value.IsNumber)
        {
            number = (decimal)Value.GetNumber();
            return true;
        }

        return decimal.TryParse(Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);
    }

    public bool TryInteger(out long number)
    {
        if (TryDecimal(out var value) && decimal.Truncate(value) == value)
        {
            number = (long)value;
            return true;
        }

        number = 0;
        return false;
    }

    public bool TryBoolean(out bool flag)
    {
        if (Value.IsBoolean)
        {
            flag = Value.GetBoolean();
            return true;
        }

        flag = Text.ToLowerInvariant() is "true" or "yes" or "1";

        return flag || Text.ToLowerInvariant() is "false" or "no" or "0";
    }

    public bool TryDate(out DateOnly date)
    {
        if (Value.IsDateTime)
        {
            date = DateOnly.FromDateTime(Value.GetDateTime());
            return true;
        }

        return DateOnly.TryParseExact(Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}

internal sealed class ImportFileException(string message) : Exception(message);

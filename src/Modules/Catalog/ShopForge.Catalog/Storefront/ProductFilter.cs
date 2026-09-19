using System.Globalization;
using ShopForge.Catalog.Attributes;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Storefront;

// Query syntax: f.<code>=a,b for options, f.<code>=min..max (either side optional) for numbers and dates, f.<code>=true|false.
internal sealed record ProductFilter(AttributeDefinition Definition)
{
    public const string QueryPrefix = "f.";

    public IReadOnlyList<Guid> OptionIds { get; private init; } = [];

    public long? IntegerMin { get; private init; }

    public long? IntegerMax { get; private init; }

    public decimal? DecimalMin { get; private init; }

    public decimal? DecimalMax { get; private init; }

    public DateOnly? DateMin { get; private init; }

    public DateOnly? DateMax { get; private init; }

    public bool? Boolean { get; private init; }

    public object? SelectedMin => IntegerMin ?? DecimalMin ?? (object?)DateMin;

    public object? SelectedMax => IntegerMax ?? DecimalMax ?? (object?)DateMax;

    public static ProductFilter? Parse(AttributeDefinition definition, string text)
    {
        var filter = new ProductFilter(definition);

        switch (definition.Type)
        {
            case AttributeType.Select or AttributeType.MultiSelect:
                var options = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(code => definition.Options.SingleOrDefault(option => option.Code == code))
                    .ToList();
                return options.Count > 0 && options.All(option => option is not null)
                    ? filter with { OptionIds = [.. options.Select(option => option!.Id).Distinct()] }
                    : null;
            case AttributeType.Boolean when bool.TryParse(text, out var boolean):
                return filter with { Boolean = boolean };
            case AttributeType.Integer when TryParseRange<long>(text, long.TryParse, out var min, out var max):
                return filter with { IntegerMin = min, IntegerMax = max };
            case AttributeType.Decimal when TryParseRange<decimal>(text, TryParseDecimal, out var min, out var max):
                return filter with { DecimalMin = min, DecimalMax = max };
            case AttributeType.Date when TryParseRange<DateOnly>(text, AttributeValueJson.TryParseDate, out var min, out var max):
                return filter with { DateMin = min, DateMax = max };
            default:
                return null;
        }
    }

    private delegate bool TryParser<T>(string text, out T value);

    private static bool TryParseRange<T>(string text, TryParser<T> parse, out T? min, out T? max)
        where T : struct
    {
        min = null;
        max = null;
        var parts = text.Split("..");

        if (parts.Length > 2 || parts.All(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        if (parts.Length == 1)
        {
            parts = [parts[0], parts[0]];
        }

        if (parts[0].Length > 0)
        {
            if (!parse(parts[0], out var value))
            {
                return false;
            }

            min = value;
        }

        if (parts[1].Length > 0)
        {
            if (!parse(parts[1], out var value))
            {
                return false;
            }

            max = value;
        }

        return true;
    }

    private static bool TryParseDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
}

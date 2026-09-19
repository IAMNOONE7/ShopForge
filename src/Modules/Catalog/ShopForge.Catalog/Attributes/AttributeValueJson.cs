using System.Globalization;
using System.Text.Json;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Attributes;

// JSON shape of attribute values in the API: text, integer and decimal numbers, true/false, "yyyy-MM-dd" dates,
// an option code for Select and an array of option codes for MultiSelect.
internal static class AttributeValueJson
{
    public static bool TryRead(AttributeDefinition definition, JsonElement json, out AttributeValue value)
    {
        value = new AttributeValue();

        switch (definition.Type)
        {
            case AttributeType.Text when json.ValueKind == JsonValueKind.String && json.GetString()!.Trim() is { Length: > 0 and <= AttributeDefinition.MaxTextLength } text:
                value = new AttributeValue { Text = text };
                return true;
            case AttributeType.Integer when json.ValueKind == JsonValueKind.Number && json.TryGetInt64(out var integer):
                value = new AttributeValue { Integer = integer };
                return true;
            case AttributeType.Decimal when json.ValueKind == JsonValueKind.Number && json.TryGetDecimal(out var number):
                value = new AttributeValue { Decimal = number };
                return true;
            case AttributeType.Boolean when json.ValueKind is JsonValueKind.True or JsonValueKind.False:
                value = new AttributeValue { Boolean = json.GetBoolean() };
                return true;
            case AttributeType.Date when json.ValueKind == JsonValueKind.String && TryParseDate(json.GetString(), out var date):
                value = new AttributeValue { Date = date };
                return true;
            case AttributeType.Select when json.ValueKind == JsonValueKind.String && FindOption(definition, json.GetString()) is { } option:
                value = new AttributeValue { OptionIds = [option.Id] };
                return true;
            case AttributeType.MultiSelect when json.ValueKind == JsonValueKind.Array:
                var options = json.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? FindOption(definition, item.GetString()) : null)
                    .ToList();
                if (options.Count == 0 || options.Any(option => option is null) || options.Distinct().Count() != options.Count)
                {
                    return false;
                }

                value = new AttributeValue { OptionIds = [.. options.Select(option => option!.Id)] };
                return true;
            default:
                return false;
        }
    }

    public static object? Write(AttributeDefinition definition, IReadOnlyCollection<ProductAttributeValue> rows, bool optionNames)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var row = rows.First();
        var options = definition.Options
            .Where(option => rows.Any(candidate => candidate.OptionId == option.Id))
            .OrderBy(option => option.SortOrder)
            .Select(option => optionNames ? option.Name : option.Code)
            .ToList();

        return definition.Type switch
        {
            AttributeType.Text => row.TextValue,
            AttributeType.Integer => row.IntegerValue,
            AttributeType.Decimal => row.DecimalValue,
            AttributeType.Boolean => row.BooleanValue,
            AttributeType.Date => row.DateValue,
            AttributeType.Select => options.SingleOrDefault(),
            AttributeType.MultiSelect => options,
            _ => null,
        };
    }

    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static AttributeOption? FindOption(AttributeDefinition definition, string? code) =>
        definition.Options.SingleOrDefault(option => option.Code == code);
}

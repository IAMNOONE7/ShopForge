using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Import;

internal static class AttributeCells
{
    // Options named in the file but missing from the attribute are created, so a store can grow its option lists by importing.
    public static bool TryRead(AttributeDefinition definition, ImportCell cell, out AttributeValue value)
    {
        value = new AttributeValue();

        switch (definition.Type)
        {
            case AttributeType.Text when cell.Text.Length <= AttributeDefinition.MaxTextLength:
                value = new AttributeValue { Text = cell.Text };
                return true;
            case AttributeType.Integer when cell.TryInteger(out var integer):
                value = new AttributeValue { Integer = integer };
                return true;
            case AttributeType.Decimal when cell.TryDecimal(out var number):
                value = new AttributeValue { Decimal = number };
                return true;
            case AttributeType.Boolean when cell.TryBoolean(out var flag):
                value = new AttributeValue { Boolean = flag };
                return true;
            case AttributeType.Date when cell.TryDate(out var date):
                value = new AttributeValue { Date = date };
                return true;
            case AttributeType.Select or AttributeType.MultiSelect:
                var names = definition.Type == AttributeType.Select ? [cell.Text] : CatalogImporter.Split(cell.Text).ToList();
                var options = names.Select(name => Option(definition, name)).ToList();

                if (names.Count == 0 || options.Any(option => option is null) || options.Distinct().Count() != options.Count)
                {
                    return false;
                }

                value = new AttributeValue { OptionIds = [.. options.Select(option => option!.Id)] };
                return true;
            default:
                return false;
        }
    }

    public static string Expected(AttributeDefinition definition) => definition.Type switch
    {
        AttributeType.Text => $"Expected text of up to {AttributeDefinition.MaxTextLength} characters.",
        AttributeType.Integer => "Expected a whole number.",
        AttributeType.Decimal => "Expected a number.",
        AttributeType.Boolean => "Expected true or false.",
        AttributeType.Date => "Expected a date (a date cell or yyyy-MM-dd).",
        AttributeType.Select => "Expected one option name.",
        _ => "Expected a list of distinct option names.",
    };

    private static AttributeOption? Option(AttributeDefinition definition, string name)
    {
        var code = Slugs.Create(name);

        if (code.Length == 0)
        {
            return null;
        }

        return definition.Options.SingleOrDefault(option => option.Code == code) ?? definition.AddOption(name, code);
    }
}

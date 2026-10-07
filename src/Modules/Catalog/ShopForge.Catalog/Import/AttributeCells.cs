using ShopForge.Catalog.Domain;
using ShopForge.Shared.Stores;

namespace ShopForge.Catalog.Import;

internal static class AttributeCells
{
    // Reads a cell without touching the model: option names are resolved (and created) only once the whole row is valid.
    public static bool TryRead(AttributeDefinition definition, ImportCell cell, out PendingAttributeValue value)
    {
        value = new PendingAttributeValue();

        switch (definition.Type)
        {
            case AttributeType.Text when cell.Text.Length <= AttributeDefinition.MaxTextLength:
                value = new PendingAttributeValue { Value = new AttributeValue { Text = cell.Text } };
                return true;
            case AttributeType.Integer when cell.TryInteger(out var integer):
                value = new PendingAttributeValue { Value = new AttributeValue { Integer = integer } };
                return true;
            case AttributeType.Decimal when cell.TryDecimal(out var number):
                value = new PendingAttributeValue { Value = new AttributeValue { Decimal = number } };
                return true;
            case AttributeType.Boolean when cell.TryBoolean(out var flag):
                value = new PendingAttributeValue { Value = new AttributeValue { Boolean = flag } };
                return true;
            case AttributeType.Date when cell.TryDate(out var date):
                value = new PendingAttributeValue { Value = new AttributeValue { Date = date } };
                return true;
            case AttributeType.Select or AttributeType.MultiSelect:
                List<string> names = definition.Type == AttributeType.Select ? [cell.Text] : [.. CatalogImporter.Split(cell.Text)];
                var codes = names.Select(Slugs.Create).ToList();

                if (names.Count == 0 || codes.Any(code => code.Length == 0) || codes.Distinct().Count() != codes.Count)
                {
                    return false;
                }

                value = new PendingAttributeValue { OptionNames = names };
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

    // Options named in the file but missing from the attribute are created, so a store can grow its option lists by importing.
    public static AttributeValue Resolve(AttributeDefinition definition, PendingAttributeValue pending)
    {
        if (pending.Value is { } value)
        {
            return value;
        }

        var options = pending.OptionNames.Select(name =>
            definition.Options.SingleOrDefault(option => option.Code == Slugs.Create(name)) ?? definition.AddOption(name, Slugs.Create(name)));

        return new AttributeValue { OptionIds = [.. options.Select(option => option.Id)] };
    }
}

internal sealed record PendingAttributeValue
{
    public AttributeValue? Value { get; init; }

    public IReadOnlyList<string> OptionNames { get; init; } = [];
}

using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

internal sealed class AttributeDefinition : IStoreOwned
{
    public const int MaxTextLength = 500;

    private readonly List<AttributeOption> _options = [];

    private AttributeDefinition()
    {
    }

    public AttributeDefinition(Guid storeId, string code, string name, AttributeType type, AttributeSettings settings)
    {
        if (!Slugs.IsValid(code))
        {
            throw new ArgumentException($"'{code}' is not a valid attribute code.", nameof(code));
        }

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Code = code;
        Type = type;
        Update(name, settings);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public AttributeType Type { get; private set; }

    public string? Unit { get; private set; }

    public bool IsFilterable { get; private set; }

    public bool IsVisibleOnProductPage { get; private set; }

    // Whether this goes into a shopping feed as a parameter. Separate from being visible on the page, because
    // a shop may want a measurement in a comparison table that it does not clutter its own page with — and the
    // other way round (D-170).
    public bool IsInFeeds { get; private set; }

    public int SortOrder { get; private set; }

    public IReadOnlyList<AttributeOption> Options => _options;

    public bool HasOptions => Type is AttributeType.Select or AttributeType.MultiSelect;

    public void Update(string name, AttributeSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (settings.IsFilterable && Type == AttributeType.Text)
        {
            throw new ArgumentException("Text attributes cannot be used as filters.", nameof(settings));
        }

        Name = name.Trim();
        Unit = string.IsNullOrWhiteSpace(settings.Unit) ? null : settings.Unit.Trim();
        IsFilterable = settings.IsFilterable;
        IsVisibleOnProductPage = settings.IsVisibleOnProductPage;
        IsInFeeds = settings.IsInFeeds;
        SortOrder = settings.SortOrder;
    }

    public AttributeOption AddOption(string name, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!HasOptions)
        {
            throw new InvalidOperationException($"{Type} attributes have no options.");
        }

        if (!Slugs.IsValid(code) || _options.Any(option => option.Code == code))
        {
            throw new ArgumentException($"'{code}' is not a valid or unique option code.", nameof(code));
        }

        var option = new AttributeOption(StoreId, Id, code, name.Trim(), _options.Count);
        _options.Add(option);
        return option;
    }

    public IEnumerable<ProductAttributeValue> CreateValues(Guid storeProductId, AttributeValue value)
    {
        var expected = Type switch
        {
            AttributeType.Text => value.Text is { Length: > 0 and <= MaxTextLength },
            AttributeType.Integer => value.Integer is not null,
            AttributeType.Decimal => value.Decimal is not null,
            AttributeType.Boolean => value.Boolean is not null,
            AttributeType.Date => value.Date is not null,
            AttributeType.Select => value.OptionIds.Count == 1,
            AttributeType.MultiSelect => value.OptionIds.Count > 0 && value.OptionIds.Distinct().Count() == value.OptionIds.Count,
            _ => false,
        };

        if (!expected || (HasOptions && value.OptionIds.Any(id => _options.All(option => option.Id != id))))
        {
            throw new ArgumentException($"The value does not match the {Type} attribute '{Code}'.", nameof(value));
        }

        return HasOptions
            ? value.OptionIds.Select(optionId => new ProductAttributeValue(StoreId, storeProductId, Id, new AttributeValue { OptionIds = [optionId] }))
            : [new ProductAttributeValue(StoreId, storeProductId, Id, value)];
    }
}

internal sealed record AttributeSettings(string? Unit, bool IsFilterable, bool IsVisibleOnProductPage, int SortOrder, bool IsInFeeds = false);

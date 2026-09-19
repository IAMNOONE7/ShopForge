namespace ShopForge.Catalog.Domain;

internal sealed record AttributeValue
{
    public string? Text { get; init; }

    public long? Integer { get; init; }

    public decimal? Decimal { get; init; }

    public bool? Boolean { get; init; }

    public DateOnly? Date { get; init; }

    public IReadOnlyList<Guid> OptionIds { get; init; } = [];
}

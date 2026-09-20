using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

internal sealed class ProductAttributeValue : IStoreOwned
{
    private ProductAttributeValue()
    {
    }

    internal ProductAttributeValue(Guid storeId, Guid storeProductId, Guid attributeDefinitionId, AttributeValue value)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        StoreProductId = storeProductId;
        AttributeDefinitionId = attributeDefinitionId;
        TextValue = value.Text;
        IntegerValue = value.Integer;
        DecimalValue = value.Decimal;
        BooleanValue = value.Boolean;
        DateValue = value.Date;
        OptionId = value.OptionIds is [var optionId] ? optionId : null;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid StoreProductId { get; private set; }

    public Guid AttributeDefinitionId { get; private set; }

    public string? TextValue { get; private set; }

    public long? IntegerValue { get; private set; }

    public decimal? DecimalValue { get; private set; }

    public bool? BooleanValue { get; private set; }

    public DateOnly? DateValue { get; private set; }

    public Guid? OptionId { get; private set; }

    internal bool HasSameValueAs(ProductAttributeValue other) =>
        TextValue == other.TextValue
        && IntegerValue == other.IntegerValue
        && DecimalValue == other.DecimalValue
        && BooleanValue == other.BooleanValue
        && DateValue == other.DateValue
        && OptionId == other.OptionId;
}

using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

internal sealed class AttributeOption : IStoreOwned
{
    private AttributeOption()
    {
    }

    internal AttributeOption(Guid storeId, Guid attributeDefinitionId, string code, string name, int sortOrder)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        AttributeDefinitionId = attributeDefinitionId;
        Code = code;
        Name = name;
        SortOrder = sortOrder;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid AttributeDefinitionId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public int SortOrder { get; private set; }
}

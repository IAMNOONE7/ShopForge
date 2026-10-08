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

    // The name is a label and the code is an address: the code turns up in a filter somebody has bookmarked
    // and in a feed somebody else publishes, so correcting a spelling changes the words and not the address
    // (D-181).
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
    }

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;
}

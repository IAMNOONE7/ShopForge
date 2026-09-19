using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

internal sealed class Category : IStoreOwned
{
    private readonly List<CategoryAttribute> _attributes = [];

    private Category()
    {
    }

    public Category(Guid storeId, string name, string slug, int sortOrder)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Update(name, slug, sortOrder);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Slug { get; private set; } = null!;

    public int SortOrder { get; private set; }

    public IReadOnlyCollection<CategoryAttribute> Attributes => _attributes;

    public void Update(string name, string slug, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!Slugs.IsValid(slug))
        {
            throw new ArgumentException($"'{slug}' is not a valid slug.", nameof(slug));
        }

        Name = name.Trim();
        Slug = slug;
        SortOrder = sortOrder;
    }

    public void AssignAttributes(IReadOnlyList<AttributeDefinition> definitions)
    {
        if (definitions.Any(definition => definition.StoreId != StoreId))
        {
            throw new InvalidOperationException("Attributes must belong to the category's store.");
        }

        _attributes.RemoveAll(assignment => definitions.All(definition => definition.Id != assignment.AttributeDefinitionId));

        for (var position = 0; position < definitions.Count; position++)
        {
            var existing = _attributes.SingleOrDefault(assignment => assignment.AttributeDefinitionId == definitions[position].Id);

            if (existing is null)
            {
                _attributes.Add(new CategoryAttribute(StoreId, Id, definitions[position].Id, position));
            }
            else
            {
                existing.MoveTo(position);
            }
        }
    }
}

using ShopForge.Shared.Catalog;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

internal sealed class Category : IStoreOwned, IArchivable
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
    // Retired: gone from the shop and from the merchant's own list unless they ask for it (D-180).
    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public bool Archive(DateTimeOffset at)
    {
        if (IsArchived)
        {
            return false;
        }

        ArchivedAt = at;

        return true;
    }

    public bool Restore()
    {
        if (!IsArchived)
        {
            return false;
        }

        ArchivedAt = null;

        return true;
    }

    // Who this one sits under, or nothing for a category at the top. The rules about where it may sit need the
    // other categories to answer, so they live in CategoryTree and this only refuses the one a category can
    // see for itself (D-172).
    public Guid? ParentId { get; private set; }

    public void MoveTo(Guid? parentId)
    {
        if (parentId == Id)
        {
            throw new InvalidOperationException("A category cannot be its own parent.");
        }

        ParentId = parentId;
    }

    // A category's own answer, and the words a merchant writes above the products — which 28f puts on the page
    // and which is the only thing on a category page a crawler can read that is not a list (D-165).
    public string? SeoTitle { get; private set; }

    public string? SeoDescription { get; private set; }

    public string? PageText { get; private set; }

    public bool DescribeToSearchEngines(string? title, string? description, string? pageText)
    {
        var tidied = (Tidied(title), Tidied(description), Tidied(pageText));

        if (tidied == (SeoTitle, SeoDescription, PageText))
        {
            return false;
        }

        (SeoTitle, SeoDescription, PageText) = tidied;

        return true;
    }

    private static string? Tidied(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

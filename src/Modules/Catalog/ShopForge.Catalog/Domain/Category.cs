using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

internal sealed class Category : IStoreOwned
{
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
}

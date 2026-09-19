using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

internal sealed class StoreProduct : IStoreOwned
{
    private readonly List<ProductCategory> _categories = [];

    private StoreProduct()
    {
    }

    public StoreProduct(Guid storeId, Product product, StoreProductDetails details)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        ProductId = product.Id;
        Update(details);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid ProductId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Slug { get; private set; } = null!;

    public string? Description { get; private set; }

    public decimal Price { get; private set; }

    public bool IsVisible { get; private set; }

    public int SortOrder { get; private set; }

    public IReadOnlyCollection<ProductCategory> Categories => _categories;

    public void Update(StoreProductDetails details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(details.Name);

        if (details.Price < 0 || decimal.Round(details.Price, 2) != details.Price)
        {
            throw new ArgumentOutOfRangeException(nameof(details), "Price must be a non-negative amount with at most two decimals.");
        }

        if (!Slugs.IsValid(details.Slug))
        {
            throw new ArgumentException($"'{details.Slug}' is not a valid slug.", nameof(details));
        }

        Name = details.Name.Trim();
        Slug = details.Slug;
        Description = string.IsNullOrWhiteSpace(details.Description) ? null : details.Description.Trim();
        Price = details.Price;
        IsVisible = details.IsVisible;
        SortOrder = details.SortOrder;
    }

    public void AssignCategories(IReadOnlyCollection<Category> categories)
    {
        if (categories.Any(category => category.StoreId != StoreId))
        {
            throw new InvalidOperationException("Categories must belong to the product's store.");
        }

        _categories.RemoveAll(assignment => categories.All(category => category.Id != assignment.CategoryId));

        foreach (var category in categories.Where(category => _categories.All(assignment => assignment.CategoryId != category.Id)))
        {
            _categories.Add(new ProductCategory(StoreId, Id, category.Id));
        }
    }
}

internal sealed record StoreProductDetails(string Name, string Slug, string? Description, decimal Price, bool IsVisible, int SortOrder);

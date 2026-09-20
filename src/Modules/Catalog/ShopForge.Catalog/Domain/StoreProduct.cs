using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

internal sealed class StoreProduct : IStoreOwned
{
    private readonly List<ProductCategory> _categories = [];
    private readonly List<ProductAttributeValue> _attributeValues = [];

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

    public decimal VatRate { get; private set; }

    public bool IsVisible { get; private set; }

    public int SortOrder { get; private set; }

    public IReadOnlyCollection<ProductCategory> Categories => _categories;

    public IReadOnlyCollection<ProductAttributeValue> AttributeValues => _attributeValues;

    public bool Update(StoreProductDetails details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(details.Name);

        if (details.Price < 0 || decimal.Round(details.Price, 2) != details.Price)
        {
            throw new ArgumentOutOfRangeException(nameof(details), "Price must be a non-negative amount with at most two decimals.");
        }

        if (details.VatRate is < 0 or > 100 || decimal.Round(details.VatRate, 2) != details.VatRate)
        {
            throw new ArgumentOutOfRangeException(nameof(details), "The VAT rate must be between 0 and 100 with at most two decimals.");
        }

        if (!Slugs.IsValid(details.Slug))
        {
            throw new ArgumentException($"'{details.Slug}' is not a valid slug.", nameof(details));
        }

        var updated = new StoreProductDetails(
            details.Name.Trim(),
            details.Slug,
            string.IsNullOrWhiteSpace(details.Description) ? null : details.Description.Trim(),
            details.Price,
            details.VatRate,
            details.IsVisible,
            details.SortOrder);

        if (updated == Current)
        {
            return false;
        }

        Name = updated.Name;
        Slug = updated.Slug;
        Description = updated.Description;
        Price = updated.Price;
        VatRate = updated.VatRate;
        IsVisible = updated.IsVisible;
        SortOrder = updated.SortOrder;

        return true;
    }

    public StoreProductDetails Current => new(Name, Slug, Description, Price, VatRate, IsVisible, SortOrder);

    // Import sets only the attributes present in the file; values of other attributes stay as they are.
    public bool SetAttributeValue(AttributeDefinition definition, AttributeValue value)
    {
        if (definition.StoreId != StoreId)
        {
            throw new InvalidOperationException("Attributes must belong to the product's store.");
        }

        var replacement = definition.CreateValues(Id, value).ToList();
        var existing = _attributeValues.Where(current => current.AttributeDefinitionId == definition.Id).ToList();

        if (existing.Count == replacement.Count && existing.All(current => replacement.Any(candidate => candidate.HasSameValueAs(current))))
        {
            return false;
        }

        _attributeValues.RemoveAll(existing.Contains);
        _attributeValues.AddRange(replacement);

        return true;
    }

    public bool AddToCategories(IReadOnlyCollection<Category> categories)
    {
        if (categories.Any(category => category.StoreId != StoreId))
        {
            throw new InvalidOperationException("Categories must belong to the product's store.");
        }

        var missing = categories.Where(category => _categories.All(assignment => assignment.CategoryId != category.Id)).ToList();
        _categories.AddRange(missing.Select(category => new ProductCategory(StoreId, Id, category.Id)));

        return missing.Count > 0;
    }

    public void ReplaceAttributeValues(IReadOnlyCollection<(AttributeDefinition Definition, AttributeValue Value)> values)
    {
        if (values.Any(item => item.Definition.StoreId != StoreId))
        {
            throw new InvalidOperationException("Attributes must belong to the product's store.");
        }

        if (values.Select(item => item.Definition.Id).Distinct().Count() != values.Count)
        {
            throw new ArgumentException("Each attribute can be set only once.", nameof(values));
        }

        _attributeValues.Clear();
        _attributeValues.AddRange(values.SelectMany(item => item.Definition.CreateValues(Id, item.Value)));
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

internal sealed record StoreProductDetails(string Name, string Slug, string? Description, decimal Price, decimal VatRate, bool IsVisible, int SortOrder);

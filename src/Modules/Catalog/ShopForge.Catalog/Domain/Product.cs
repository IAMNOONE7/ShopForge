using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

internal sealed class Product : ITenantOwned
{
    private readonly List<ProductImage> _images = [];

    private Product()
    {
    }

    public Product(Guid tenantId, string sku, string? ean, int? weightGrams)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Sku = sku.Trim().ToUpperInvariant();
        UpdatePhysicalData(ean, weightGrams);
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Sku { get; private set; } = null!;

    public string? Ean { get; private set; }

    public int? WeightGrams { get; private set; }

    public IReadOnlyList<ProductImage> Images => _images;

    public bool UpdatePhysicalData(string? ean, int? weightGrams)
    {
        if (weightGrams is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weightGrams), "Weight cannot be negative.");
        }

        var updatedEan = string.IsNullOrWhiteSpace(ean) ? null : ean.Trim();

        if (updatedEan == Ean && weightGrams == WeightGrams)
        {
            return false;
        }

        Ean = updatedEan;
        WeightGrams = weightGrams;

        return true;
    }

    public ProductImage AddImage(string contentType, string? altText)
    {
        var image = new ProductImage(Id, TenantId, contentType, altText, position: _images.Count);
        _images.Add(image);
        return image;
    }

    public ProductImage? RemoveImage(Guid imageId)
    {
        var image = _images.SingleOrDefault(image => image.Id == imageId);

        if (image is not null)
        {
            _images.Remove(image);

            for (var position = 0; position < _images.Count; position++)
            {
                _images[position].MoveTo(position);
            }
        }

        return image;
    }
}

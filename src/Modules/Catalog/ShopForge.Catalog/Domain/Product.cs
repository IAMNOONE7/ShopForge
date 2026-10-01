using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

// What a shopper looks at. What they buy is one of its variants, and there is always at least one: a product
// sold in a single form is a product with a single variant, which is what every product was before (D-134).
internal sealed class Product : ITenantOwned
{
    public const int MaxOptions = 3;

    private readonly List<ProductImage> _images = [];
    private readonly List<ProductVariant> _variants = [];

    private Product()
    {
    }

    public Product(Guid tenantId, string sku, string? ean, int? weightGrams)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        AddVariant(sku, ean, weightGrams, []);
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    // The axes this product is sold along — ["Size", "Colour"] — empty for a product sold in one form only.
    public string[] OptionNames { get; private set; } = [];

    public IReadOnlyList<ProductImage> Images => _images;

    public IReadOnlyList<ProductVariant> Variants => _variants;

    // Everything written before variants existed asks a product for its SKU and its weight, and a shop that
    // sells one form of a thing still thinks that way. It is the first variant's.
    public ProductVariant Default => _variants.OrderBy(variant => variant.Position).First();

    public ProductVariant AddVariant(string sku, string? ean, int? weightGrams, string[] optionValues)
    {
        var variant = new ProductVariant(TenantId, Id, sku, ean, weightGrams, optionValues, _variants.Count);
        _variants.Add(variant);

        return variant;
    }

    // The last variant cannot go: a product nobody can buy is not a product, it is a listing with nothing behind it.
    public bool RemoveVariant(Guid variantId)
    {
        if (_variants.Count < 2 || _variants.SingleOrDefault(variant => variant.Id == variantId) is not { } variant)
        {
            return false;
        }

        _variants.Remove(variant);

        for (var position = 0; position < _variants.Count; position++)
        {
            _variants[position].MoveTo(position);
        }

        return true;
    }

    // Changing the axes changes what every variant has to say about itself, so the values come with the names.
    public void SetOptions(string[] names, IReadOnlyDictionary<Guid, string[]> valuesByVariant)
    {
        OptionNames = names;

        foreach (var variant in _variants)
        {
            variant.Choose(valuesByVariant.TryGetValue(variant.Id, out var values) ? values : new string[names.Length]);
        }
    }

    // Importing brings one row per form, so the axes are named once and each row says where its own form sits
    // along them, rather than every row restating the whole product (D-137).
    public bool SellAlong(string[] names, ProductVariant variant, string[] values)
    {
        var changed = false;

        if (!OptionNames.SequenceEqual(names, StringComparer.Ordinal))
        {
            OptionNames = names;
            changed = true;
        }

        if (!variant.OptionValues.SequenceEqual(values, StringComparer.Ordinal))
        {
            variant.Choose(values);
            changed = true;
        }

        return changed;
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

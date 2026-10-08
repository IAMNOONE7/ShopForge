using ShopForge.Shared.Catalog;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

// What a shopper looks at. What they buy is one of its variants, and there is always at least one: a product
// sold in a single form is a product with a single variant, which is what every product was before (D-134).
internal sealed class Product : ITenantOwned, IArchivable
{
    public const int MaxOptions = 3;
    public const int MaxBrandLength = 70;

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

    // Who made the thing. It belongs to the product rather than to the shop selling it or to one of its
    // sizes, and every shopping feed asks for it (D-163). Null until somebody says.
    public string? Brand { get; private set; }
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

    // The axes this product is sold along — ["Size", "Colour"] — empty for a product sold in one form only.
    public string[] OptionNames { get; private set; } = [];

    public IReadOnlyList<ProductImage> Images => _images;

    public IReadOnlyList<ProductVariant> Variants => _variants;

    // Everything written before variants existed asks a product for its SKU and its weight, and a shop that
    // sells one form of a thing still thinks that way. It is the first variant's.
    public ProductVariant Default => _variants.OrderBy(variant => variant.Position).First();

    public bool Rebrand(string? brand)
    {
        var named = string.IsNullOrWhiteSpace(brand) ? null : brand.Trim();

        if (named == Brand)
        {
            return false;
        }

        Brand = named;

        return true;
    }

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

    public ProductImage? Image(Guid imageId) => _images.SingleOrDefault(image => image.Id == imageId);

    // The order a shopper flicks through them in, which is also which one is the thumbnail: the first
    // picture is the one a card and a feed show (D-169, D-182). Anything the caller does not name keeps its
    // order behind the ones it does, the same way an attribute's options do (D-181).
    public void ReorderImages(IReadOnlyList<Guid> order)
    {
        var position = 0;

        foreach (var id in order)
        {
            if (_images.SingleOrDefault(image => image.Id == id) is { } named)
            {
                named.MoveTo(position++);
            }
        }

        foreach (var rest in Remaining(order))
        {
            rest.MoveTo(position++);
        }
    }

    private List<ProductImage> Remaining(IReadOnlyList<Guid> order) =>
        [.. _images.Where(image => !order.Contains(image.Id)).OrderBy(image => image.Position)];

    public ProductImage? RemoveImage(Guid imageId)
    {
        var image = _images.SingleOrDefault(image => image.Id == imageId);

        if (image is not null)
        {
            _images.Remove(image);

            // In the order they were in, not the order they happen to be loaded in: renumbering by the list's
            // own order would reshuffle the remaining pictures every time one was deleted (D-182).
            var position = 0;

            foreach (var kept in _images.OrderBy(kept => kept.Position).ToList())
            {
                kept.MoveTo(position++);
            }
        }

        return image;
    }
}

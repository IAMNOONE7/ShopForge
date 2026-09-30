using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

// The thing a shop actually stocks, scans and sells: one size of a shirt, one colour of a chair. A product is
// what a shopper looks at; a variant is what leaves the warehouse, so the SKU, the barcode and the weight belong
// here rather than on the product (D-134).
internal sealed class ProductVariant : ITenantOwned
{
    public const int MaxSkuLength = 64;

    private ProductVariant()
    {
    }

    internal ProductVariant(Guid tenantId, Guid productId, string sku, string? ean, int? weightGrams, string[] optionValues, int position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        ProductId = productId;
        Sku = Normalise(sku);
        Position = position;
        OptionValues = optionValues;
        UpdatePhysicalData(ean, weightGrams);
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid ProductId { get; private set; }

    public string Sku { get; private set; } = null!;

    public string? Ean { get; private set; }

    public int? WeightGrams { get; private set; }

    // One value per option the product declares, in the same order: ["M", "Red"] against ["Size", "Colour"].
    public string[] OptionValues { get; private set; } = [];

    public int Position { get; private set; }

    public static string Normalise(string sku) => sku.Trim().ToUpperInvariant();

    public void Rename(string sku) => Sku = Normalise(sku);

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

    internal void Choose(string[] optionValues) => OptionValues = optionValues;

    internal void MoveTo(int position) => Position = position;
}

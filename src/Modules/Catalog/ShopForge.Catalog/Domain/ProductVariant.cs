using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

// The thing a shop actually stocks, scans and sells: one size of a shirt, one colour of a chair. A product is
// what a shopper looks at; a variant is what leaves the warehouse, so the SKU, the barcode and the weight belong
// here rather than on the product (D-134).
internal sealed class ProductVariant : ITenantOwned
{
    public const int MaxSkuLength = 64;
    public const int MaxPartNumberLength = 70;

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

    // What the manufacturer calls this exact form of the thing, which is not what we call it: the SKU is the
    // shop's own name for a shelf. A feed will take one or the other and prefers both (D-163).
    public string? PartNumber { get; private set; }

    // New, refurbished or used. Null means nobody has said, which is not the same as new — a shop selling
    // second-hand goods must not have them declared new by a default nobody chose.
    public ProductCondition? Condition { get; private set; }

    // One value per option the product declares, in the same order: ["M", "Red"] against ["Size", "Colour"].
    public string[] OptionValues { get; private set; } = [];

    public int Position { get; private set; }

    public static string Normalise(string sku) => sku.Trim().ToUpperInvariant();

    public void Rename(string sku) => Sku = Normalise(sku);

    public bool UpdatePhysicalData(string? ean, int? weightGrams) =>
        UpdatePhysicalData(ean, weightGrams, PartNumber, Condition);

    public bool UpdatePhysicalData(string? ean, int? weightGrams, string? partNumber, ProductCondition? condition)
    {
        if (weightGrams is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weightGrams), "Weight cannot be negative.");
        }

        if (!Gtin.IsValid(ean))
        {
            throw new ArgumentException("A barcode must be a GTIN whose check digit agrees.", nameof(ean));
        }

        var updatedEan = string.IsNullOrWhiteSpace(ean) ? null : ean.Trim();
        var updatedPartNumber = string.IsNullOrWhiteSpace(partNumber) ? null : partNumber.Trim();

        if (updatedEan == Ean && weightGrams == WeightGrams && updatedPartNumber == PartNumber && condition == Condition)
        {
            return false;
        }

        Ean = updatedEan;
        WeightGrams = weightGrams;
        PartNumber = updatedPartNumber;
        Condition = condition;

        return true;
    }

    internal void Choose(string[] optionValues) => OptionValues = optionValues;

    internal void MoveTo(int position) => Position = position;
}

// What a feed means by condition. Three words because that is what every feed takes, and a shop that sells
// something in another state is describing it, not categorising it.
internal enum ProductCondition
{
    New,
    Refurbished,
    Used,
}

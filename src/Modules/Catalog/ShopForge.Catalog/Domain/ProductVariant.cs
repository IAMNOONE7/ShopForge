using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Domain;

// The thing a shop actually stocks, scans and sells: one size of a shirt, one colour of a chair. A product is
// what a shopper looks at; a variant is what leaves the warehouse, so the SKU, the barcode and the weight belong
// here rather than on the product (D-134).
internal sealed class ProductVariant : ITenantOwned
{
    public const int MaxSkuLength = 64;
    public const int MaxPartNumberLength = 70;

    // Ten metres: no parcel comes near it, so a size beyond it is a slip of the keyboard.
    public const int MaxSizeMm = 10_000;

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

    // What the form measures as it is sent, in whole millimetres, all three or none. A carrier prices a parcel by
    // its weight and refuses one by its size, so delivery cannot be priced for a form without them (D-214).
    public int? LengthMm { get; private set; }

    public int? WidthMm { get; private set; }

    public int? HeightMm { get; private set; }

    // A form that travels as it is, in its own carton, rather than sharing a parcel with anything else.
    public bool ShipsAlone { get; private set; }

    // Everything a parcel is priced from: a weight of something, since no carrier takes a parcel that weighs
    // nothing, and all three sizes.
    public bool IsMeasured => WeightGrams is > 0 && LengthMm is not null && WidthMm is not null && HeightMm is not null;

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

    public static bool IsSize(int millimetres) => millimetres is > 0 and <= MaxSizeMm;

    public bool Measure(int lengthMm, int widthMm, int heightMm)
    {
        if (!IsSize(lengthMm) || !IsSize(widthMm) || !IsSize(heightMm))
        {
            throw new ArgumentOutOfRangeException(nameof(lengthMm), $"Each size must be a whole number of millimetres from 1 to {MaxSizeMm}.");
        }

        if (lengthMm == LengthMm && widthMm == WidthMm && heightMm == HeightMm)
        {
            return false;
        }

        LengthMm = lengthMm;
        WidthMm = widthMm;
        HeightMm = heightMm;

        return true;
    }

    public bool SendAlone(bool shipsAlone)
    {
        if (shipsAlone == ShipsAlone)
        {
            return false;
        }

        ShipsAlone = shipsAlone;

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

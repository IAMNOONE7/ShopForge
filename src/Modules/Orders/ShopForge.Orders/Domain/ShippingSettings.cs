using ShopForge.Orders.Shipping;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Domain;

// What a store's parcels hold besides its goods, and how many it sends (D-218): what its packaging weighs, how full of
// goods a parcel may be, and the largest order it sends by parcel before arranging delivery by hand. A store that has
// never set them has the defaults, and a row only once it changes one.
internal sealed class ShippingSettings : IStoreOwned
{
    public const int DefaultPackagingWeightGrams = 300;
    public const int DefaultFillPercent = 70;
    public const int DefaultLargestParcelOrder = 10;

    // No box weighs ten kilos; a number past it is a slip of the keyboard.
    public const int MostPackagingWeightGrams = 10_000;

    private ShippingSettings()
    {
    }

    public ShippingSettings(Guid storeId)
    {
        StoreId = storeId;
    }

    public Guid StoreId { get; private set; }

    public int PackagingWeightGrams { get; private set; } = DefaultPackagingWeightGrams;

    public int FillPercent { get; private set; } = DefaultFillPercent;

    public int LargestParcelOrder { get; private set; } = DefaultLargestParcelOrder;

    public static ParcelPacking DefaultPacking => new(DefaultPackagingWeightGrams, DefaultFillPercent, DefaultLargestParcelOrder);

    public ParcelPacking Packing => new(PackagingWeightGrams, FillPercent, LargestParcelOrder);

    public void Change(int packagingWeightGrams, int fillPercent, int largestParcelOrder)
    {
        if (packagingWeightGrams is < 0 or > MostPackagingWeightGrams
            || fillPercent is < 1 or > 100
            || largestParcelOrder is < 1 or > ParcelEstimator.MostParcels)
        {
            throw new ArgumentOutOfRangeException(nameof(packagingWeightGrams), "Packaging that weighs something sensible, a parcel filled to some share of it, and at least one parcel.");
        }

        PackagingWeightGrams = packagingWeightGrams;
        FillPercent = fillPercent;
        LargestParcelOrder = largestParcelOrder;
    }
}

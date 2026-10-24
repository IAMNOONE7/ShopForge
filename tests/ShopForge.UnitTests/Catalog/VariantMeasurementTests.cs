using ShopForge.Catalog.Domain;

namespace ShopForge.UnitTests.Catalog;

// What a form must carry before delivery can be priced for it: a weight of something and all three sizes (D-214).
public sealed class VariantMeasurementTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(450, 400, 90)]
    [InlineData(ProductVariant.MaxSizeMm, ProductVariant.MaxSizeMm, ProductVariant.MaxSizeMm)]
    public void A_form_takes_three_sizes_in_whole_millimetres(int length, int width, int height)
    {
        var variant = Variant(weightGrams: 850);

        Assert.True(variant.Measure(length, width, height));
        Assert.Equal((length, width, height), (variant.LengthMm, variant.WidthMm, variant.HeightMm));
        Assert.True(variant.IsMeasured);
    }

    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(10, -1, 10)]
    [InlineData(10, 10, ProductVariant.MaxSizeMm + 1)]
    public void A_size_of_nothing_or_beyond_ten_metres_is_refused(int length, int width, int height)
    {
        var variant = Variant(weightGrams: 850);

        Assert.Throws<ArgumentOutOfRangeException>(() => variant.Measure(length, width, height));
        Assert.Null(variant.LengthMm);
    }

    [Fact]
    public void Measuring_again_with_the_same_sizes_changes_nothing()
    {
        var variant = Variant(weightGrams: 850);
        variant.Measure(450, 400, 90);

        Assert.False(variant.Measure(450, 400, 90));
        Assert.True(variant.Measure(450, 400, 91));
    }

    // No carrier takes a parcel that weighs nothing, so a weight of nought is as good as none.
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void A_form_without_a_weight_of_something_is_not_measured(int? weightGrams)
    {
        var variant = Variant(weightGrams);
        variant.Measure(450, 400, 90);

        Assert.False(variant.IsMeasured);
    }

    [Fact]
    public void A_weighed_form_without_sizes_is_not_measured()
    {
        Assert.False(Variant(weightGrams: 850).IsMeasured);
    }

    [Fact]
    public void Shipping_alone_is_off_until_said_and_saying_it_twice_changes_nothing()
    {
        var variant = Variant(weightGrams: 850);

        Assert.False(variant.ShipsAlone);
        Assert.True(variant.SendAlone(true));
        Assert.False(variant.SendAlone(true));
        Assert.True(variant.ShipsAlone);
    }

    private static ProductVariant Variant(int? weightGrams) => new Product(Guid.NewGuid(), "SKU-1", ean: null, weightGrams).Default;
}

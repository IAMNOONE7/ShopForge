using ShopForge.Catalog.Domain;

namespace ShopForge.UnitTests.Catalog;

// A feed rejects a product whose barcode does not check out, so the shop refuses one first.
public sealed class GtinTests
{
    [Theory]
    [InlineData("96385074")]          // EAN-8
    [InlineData("036000291452")]      // UPC-A
    [InlineData("5901234123457")]     // EAN-13
    [InlineData("8594000000006")]     // EAN-13, the one the fixtures use
    [InlineData("00012345600012")]    // GTIN-14
    public void A_barcode_that_checks_out_is_accepted(string barcode) =>
        Assert.True(Gtin.IsValid(barcode));

    // One digit out is the mistake a check digit exists to catch.
    [Theory]
    [InlineData("96385075")]
    [InlineData("5901234123456")]
    [InlineData("8594000000001")]
    public void A_barcode_whose_last_digit_disagrees_is_refused(string barcode) =>
        Assert.False(Gtin.IsValid(barcode));

    [Theory]
    [InlineData("1234567")]           // too short for any GTIN
    [InlineData("123456789")]         // nine digits is not a length in use
    [InlineData("123456789012345")]   // longer than a GTIN-14
    [InlineData("59012341234a7")]
    [InlineData("5901234 123457")]
    [InlineData("")]
    public void Something_that_is_not_a_barcode_at_all_is_refused(string barcode) =>
        Assert.False(Gtin.IsValid(barcode));

    // Not every shop knows the barcode of everything it sells, and a feed only needs one for the products it
    // carries. Absent is not invalid.
    [Fact]
    public void No_barcode_is_not_a_bad_barcode() => Assert.True(Gtin.IsValid(null));

    [Fact]
    public void A_barcode_written_with_spaces_around_it_is_still_that_barcode() =>
        Assert.True(Gtin.IsValid("  5901234123457  "));
}

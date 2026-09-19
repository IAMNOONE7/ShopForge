using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Storefront;

namespace ShopForge.UnitTests.Catalog;

public sealed class ProductFilterTests
{
    private static readonly AttributeSettings Filterable = new(Unit: null, IsFilterable: true, IsVisibleOnProductPage: true, SortOrder: 0);

    [Theory]
    [InlineData("10..20", 10L, 20L)]
    [InlineData("10..", 10L, null)]
    [InlineData("..20", null, 20L)]
    [InlineData("15", 15L, 15L)]
    public void Integer_ranges_accept_open_and_closed_bounds(string text, long? min, long? max)
    {
        var filter = ProductFilter.Parse(Definition(AttributeType.Integer), text);

        Assert.Equal((min, max), (filter!.IntegerMin, filter.IntegerMax));
    }

    [Theory]
    [InlineData("Integer", "..")]
    [InlineData("Integer", "1.5..2")]
    [InlineData("Decimal", "1,5")]
    [InlineData("Date", "2024-13-01..")]
    [InlineData("Boolean", "yes")]
    [InlineData("Select", "")]
    public void Malformed_values_are_rejected(string type, string text)
    {
        Assert.Null(ProductFilter.Parse(Definition(Enum.Parse<AttributeType>(type)), text));
    }

    [Fact]
    public void Option_filters_accept_several_codes()
    {
        var material = Definition(AttributeType.Select);
        var oak = material.AddOption("Oak", "oak");
        var walnut = material.AddOption("Walnut", "walnut");

        var filter = ProductFilter.Parse(material, "oak, walnut");

        Assert.Equal([oak.Id, walnut.Id], filter!.OptionIds);
        Assert.Null(ProductFilter.Parse(material, "oak,plastic"));
    }

    private static AttributeDefinition Definition(AttributeType type) =>
        new(Guid.NewGuid(), "attribute", "Attribute", type, Filterable);
}

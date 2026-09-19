using ShopForge.Catalog.Domain;

namespace ShopForge.UnitTests.Catalog;

public sealed class AttributeDefinitionTests
{
    private static readonly AttributeSettings Filterable = new(Unit: null, IsFilterable: true, IsVisibleOnProductPage: true, SortOrder: 0);

    [Fact]
    public void Multi_select_values_become_one_row_per_option()
    {
        var colors = new AttributeDefinition(Guid.NewGuid(), "colors", "Colors", AttributeType.MultiSelect, Filterable);
        var black = colors.AddOption("Black", "black");
        var white = colors.AddOption("White", "white");

        var rows = colors.CreateValues(Guid.NewGuid(), new AttributeValue { OptionIds = [black.Id, white.Id] }).ToList();

        Assert.Equal([black.Id, white.Id], rows.Select(row => row.OptionId!.Value));
    }

    [Theory]
    [InlineData("Integer")]
    [InlineData("Boolean")]
    [InlineData("Date")]
    [InlineData("Select")]
    public void Value_of_the_wrong_type_is_rejected(string type)
    {
        var definition = new AttributeDefinition(Guid.NewGuid(), "size", "Size", Enum.Parse<AttributeType>(type), Filterable);

        Assert.Throws<ArgumentException>(() => definition.CreateValues(Guid.NewGuid(), new AttributeValue { Decimal = 1.5m }).ToList());
    }

    [Fact]
    public void Option_of_another_attribute_is_rejected()
    {
        var material = new AttributeDefinition(Guid.NewGuid(), "material", "Material", AttributeType.Select, Filterable);
        var finish = new AttributeDefinition(Guid.NewGuid(), "finish", "Finish", AttributeType.Select, Filterable);
        var matte = finish.AddOption("Matte", "matte");

        Assert.Throws<ArgumentException>(() => material.CreateValues(Guid.NewGuid(), new AttributeValue { OptionIds = [matte.Id] }).ToList());
    }

    [Fact]
    public void Text_attributes_cannot_be_filterable_and_have_no_options()
    {
        Assert.Throws<ArgumentException>(() => new AttributeDefinition(Guid.NewGuid(), "notes", "Notes", AttributeType.Text, Filterable));

        var notes = new AttributeDefinition(Guid.NewGuid(), "notes", "Notes", AttributeType.Text, Filterable with { IsFilterable = false });
        Assert.Throws<InvalidOperationException>(() => notes.AddOption("A", "a"));
    }
}

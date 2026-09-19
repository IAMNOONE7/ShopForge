using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Persistence;

internal sealed class ProductAttributeValueEntityConfiguration : IEntityTypeConfiguration<ProductAttributeValue>
{
    public void Configure(EntityTypeBuilder<ProductAttributeValue> builder)
    {
        builder.ToTable("product_attribute_values", CatalogModule.Schema, table =>
            table.HasCheckConstraint(
                "ck_product_attribute_values_single_value",
                "num_nonnulls(text_value, integer_value, decimal_value, boolean_value, date_value, option_id) = 1"));

        builder.Property(value => value.TextValue).HasMaxLength(AttributeDefinition.MaxTextLength);
        builder.Property(value => value.DecimalValue).HasPrecision(18, 4);

        builder.HasOne<AttributeDefinition>()
            .WithMany()
            .HasForeignKey(value => new { value.StoreId, value.AttributeDefinitionId })
            .HasPrincipalKey(definition => new { definition.StoreId, definition.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AttributeOption>()
            .WithMany()
            .HasForeignKey(value => new { value.StoreId, value.AttributeDefinitionId, value.OptionId })
            .HasPrincipalKey(option => new { option.StoreId, option.AttributeDefinitionId, option.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // One value per product and attribute, except multi-select, which has one row per chosen option.
        builder.HasIndex(value => new { value.StoreProductId, value.AttributeDefinitionId }, "ix_product_attribute_values_single")
            .IsUnique()
            .HasFilter("option_id IS NULL")
            .HasDatabaseName("ix_product_attribute_values_single");
        builder.HasIndex(value => new { value.StoreProductId, value.AttributeDefinitionId, value.OptionId }, "ix_product_attribute_values_option")
            .IsUnique()
            .HasFilter("option_id IS NOT NULL")
            .HasDatabaseName("ix_product_attribute_values_option");

        builder.HasIndex(value => new { value.AttributeDefinitionId, value.IntegerValue });
        builder.HasIndex(value => new { value.AttributeDefinitionId, value.DecimalValue });
        builder.HasIndex(value => new { value.AttributeDefinitionId, value.DateValue });
    }
}

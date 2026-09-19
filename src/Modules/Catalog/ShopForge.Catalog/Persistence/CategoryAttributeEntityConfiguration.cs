using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Persistence;

internal sealed class CategoryAttributeEntityConfiguration : IEntityTypeConfiguration<CategoryAttribute>
{
    public void Configure(EntityTypeBuilder<CategoryAttribute> builder)
    {
        builder.ToTable("category_attributes", CatalogModule.Schema);

        builder.HasKey(assignment => new { assignment.CategoryId, assignment.AttributeDefinitionId });

        builder.HasOne<AttributeDefinition>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.StoreId, assignment.AttributeDefinitionId })
            .HasPrincipalKey(definition => new { definition.StoreId, definition.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

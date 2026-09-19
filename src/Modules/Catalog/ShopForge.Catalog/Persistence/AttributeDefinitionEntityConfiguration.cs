using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Persistence;

internal sealed class AttributeDefinitionEntityConfiguration : IEntityTypeConfiguration<AttributeDefinition>
{
    public void Configure(EntityTypeBuilder<AttributeDefinition> builder)
    {
        builder.ToTable("attribute_definitions", CatalogModule.Schema);

        builder.HasAlternateKey(definition => new { definition.StoreId, definition.Id });

        builder.Property(definition => definition.Code).HasMaxLength(Slugs.MaxLength);
        builder.Property(definition => definition.Name).HasMaxLength(200);
        builder.Property(definition => definition.Unit).HasMaxLength(20);
        builder.Property(definition => definition.Type).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(definition => definition.HasOptions);

        builder.HasIndex(definition => new { definition.StoreId, definition.Code }).IsUnique();

        builder.HasMany(definition => definition.Options)
            .WithOne()
            .HasForeignKey(option => new { option.StoreId, option.AttributeDefinitionId })
            .HasPrincipalKey(definition => new { definition.StoreId, definition.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Stores;

namespace ShopForge.Catalog.Persistence;

internal sealed class AttributeOptionEntityConfiguration : IEntityTypeConfiguration<AttributeOption>
{
    public void Configure(EntityTypeBuilder<AttributeOption> builder)
    {
        builder.ToTable("attribute_options", CatalogModule.Schema);

        // Referenced by product values together with their attribute, so an option can only be used for its own attribute.
        builder.HasAlternateKey(option => new { option.StoreId, option.AttributeDefinitionId, option.Id });

        builder.Property(option => option.Code).HasMaxLength(Slugs.MaxLength);
        builder.Property(option => option.Name).HasMaxLength(200);

        builder.HasIndex(option => new { option.AttributeDefinitionId, option.Code }).IsUnique();
    }
}

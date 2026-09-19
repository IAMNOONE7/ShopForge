using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Persistence;

internal sealed class CategoryEntityConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories", CatalogModule.Schema);

        builder.HasAlternateKey(category => new { category.StoreId, category.Id });

        builder.Property(category => category.Name).HasMaxLength(200);
        builder.Property(category => category.Slug).HasMaxLength(Slugs.MaxLength);

        builder.HasIndex(category => new { category.StoreId, category.Slug }).IsUnique();

        builder.HasMany(category => category.Attributes)
            .WithOne()
            .HasForeignKey(assignment => new { assignment.StoreId, assignment.CategoryId })
            .HasPrincipalKey(category => new { category.StoreId, category.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

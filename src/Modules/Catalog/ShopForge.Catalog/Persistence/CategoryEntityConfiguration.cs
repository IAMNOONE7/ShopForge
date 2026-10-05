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
        builder.Property(category => category.SeoTitle).HasMaxLength(200);
        builder.Property(category => category.SeoDescription).HasMaxLength(500);

        builder.HasIndex(category => new { category.StoreId, category.Slug }).IsUnique();

        builder.HasMany(category => category.Attributes)
            .WithOne()
            .HasForeignKey(assignment => new { assignment.StoreId, assignment.CategoryId })
            .HasPrincipalKey(category => new { category.StoreId, category.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SlugHistoryEntityConfiguration : IEntityTypeConfiguration<SlugHistory>
{
    public void Configure(EntityTypeBuilder<SlugHistory> builder)
    {
        builder.ToTable("slug_history", CatalogModule.Schema);

        builder.Property(history => history.Slug).HasMaxLength(Slugs.MaxLength);
        builder.Property(history => history.Kind).HasConversion<string>().HasMaxLength(20);

        // One answer per address: a slug that is given up twice by two different rows has only its latest
        // owner to send people to.
        builder.HasIndex(history => new { history.StoreId, history.Kind, history.Slug }).IsUnique();
    }
}

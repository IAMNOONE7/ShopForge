using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Persistence;

internal sealed class ProductCategoryEntityConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("product_categories", CatalogModule.Schema);

        builder.HasKey(assignment => new { assignment.StoreProductId, assignment.CategoryId });

        // Both foreign keys include store_id, so the database itself rejects linking a product and a category of different stores.
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.StoreId, assignment.CategoryId })
            .HasPrincipalKey(category => new { category.StoreId, category.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

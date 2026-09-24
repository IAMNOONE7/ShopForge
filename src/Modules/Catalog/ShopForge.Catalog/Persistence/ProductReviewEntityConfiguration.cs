using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Persistence;

internal sealed class ProductReviewEntityConfiguration : IEntityTypeConfiguration<ProductReview>
{
    public void Configure(EntityTypeBuilder<ProductReview> builder)
    {
        builder.ToTable("product_reviews", CatalogModule.Schema, table =>
            table.HasCheckConstraint("ck_product_reviews_rating", "rating >= 1 AND rating <= 5"));

        builder.Property(review => review.Author).HasMaxLength(200);
        builder.Property(review => review.Text).HasMaxLength(ProductReview.MaxTextLength);
        builder.Property(review => review.Status).HasConversion<string>().HasMaxLength(20);

        // One review per customer and product, whatever happened to it afterwards (D-088).
        builder.HasIndex(review => new { review.StoreProductId, review.StoreCustomerId }).IsUnique();
        builder.HasIndex(review => new { review.StoreId, review.Status, review.WrittenAt });
    }
}

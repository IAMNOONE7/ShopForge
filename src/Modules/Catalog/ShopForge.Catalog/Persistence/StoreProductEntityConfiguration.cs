using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Persistence;

internal sealed class StoreProductEntityConfiguration : IEntityTypeConfiguration<StoreProduct>
{
    public void Configure(EntityTypeBuilder<StoreProduct> builder)
    {
        builder.ToTable("store_products", CatalogModule.Schema, table =>
        {
            table.HasCheckConstraint("ck_store_products_price", "price >= 0");
            table.HasCheckConstraint("ck_store_products_vat_rate", "vat_rate >= 0 AND vat_rate <= 100");
        });

        builder.HasAlternateKey(storeProduct => new { storeProduct.StoreId, storeProduct.Id });

        builder.Property(storeProduct => storeProduct.Name).HasMaxLength(200);
        builder.Property(storeProduct => storeProduct.Slug).HasMaxLength(Slugs.MaxLength);
        builder.Property(storeProduct => storeProduct.Description).HasMaxLength(10_000);
        builder.Property(storeProduct => storeProduct.SeoTitle).HasMaxLength(200);
        builder.Property(storeProduct => storeProduct.SeoDescription).HasMaxLength(500);
        builder.Property(storeProduct => storeProduct.SeoSocialImageUrl).HasMaxLength(2000);
        builder.Property(storeProduct => storeProduct.Price).HasPrecision(12, 2);
        builder.Property(storeProduct => storeProduct.VatRate).HasPrecision(5, 2);

        builder.HasIndex(storeProduct => new { storeProduct.StoreId, storeProduct.Slug }).IsUnique();
        builder.HasIndex(storeProduct => new { storeProduct.StoreId, storeProduct.ProductId }).IsUnique();

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(storeProduct => storeProduct.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(storeProduct => storeProduct.AttributeValues)
            .WithOne()
            .HasForeignKey(value => new { value.StoreId, value.StoreProductId })
            .HasPrincipalKey(storeProduct => new { storeProduct.StoreId, storeProduct.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(storeProduct => storeProduct.Categories)
            .WithOne()
            .HasForeignKey(assignment => new { assignment.StoreId, assignment.StoreProductId })
            .HasPrincipalKey(storeProduct => new { storeProduct.StoreId, storeProduct.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // Declared from this side like the others, so it shares the alternate key they already use rather than
        // asking for one under a slightly different name.
        builder.HasMany<ProductReview>()
            .WithOne()
            .HasForeignKey(review => new { review.StoreId, review.StoreProductId })
            .HasPrincipalKey(storeProduct => new { storeProduct.StoreId, storeProduct.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

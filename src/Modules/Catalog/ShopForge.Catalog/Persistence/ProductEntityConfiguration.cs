using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Catalog.Domain;

namespace ShopForge.Catalog.Persistence;

internal sealed class ProductEntityConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", CatalogModule.Schema, table =>
            table.HasCheckConstraint("ck_products_weight_grams", "weight_grams IS NULL OR weight_grams >= 0"));

        builder.Property(product => product.Sku).HasMaxLength(64);
        builder.Property(product => product.Ean).HasMaxLength(14);
        builder.HasIndex(product => new { product.TenantId, product.Sku }).IsUnique();

        builder.OwnsMany(product => product.Images, images =>
        {
            images.ToTable("product_images", CatalogModule.Schema);
            images.WithOwner().HasForeignKey("ProductId");
            images.HasKey(image => image.Id);
            images.Property(image => image.FilePath).HasMaxLength(300);
            images.Property(image => image.ContentType).HasMaxLength(50);
            images.Property(image => image.AltText).HasMaxLength(300);
        });
    }
}

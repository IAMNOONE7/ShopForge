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

        // The SKU, barcode and weight moved to the variant. The columns stay, unread and unwritten, until the
        // release after this one drops them: a change that takes data with it is two releases, never one (D-125).
        builder.Property<string?>("Sku").HasMaxLength(ProductVariant.MaxSkuLength);
        builder.Property<string?>("Ean").HasMaxLength(14);
        builder.Property<int?>("WeightGrams");

        builder.Property(product => product.OptionNames).HasColumnName("option_names");

        builder.OwnsMany(product => product.Images, images =>
        {
            images.ToTable("product_images", CatalogModule.Schema);
            images.WithOwner().HasForeignKey("ProductId");
            images.HasKey(image => image.Id);
            images.Property(image => image.FilePath).HasMaxLength(300);
            images.Property(image => image.ContentType).HasMaxLength(50);
            images.Property(image => image.AltText).HasMaxLength(300);
        });

        builder.HasMany(product => product.Variants)
            .WithOne()
            .HasForeignKey(variant => variant.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(product => product.Variants).AutoInclude();
    }
}

internal sealed class ProductVariantEntityConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants", CatalogModule.Schema, table =>
            table.HasCheckConstraint("ck_product_variants_weight_grams", "weight_grams IS NULL OR weight_grams >= 0"));

        builder.Property(variant => variant.Sku).HasMaxLength(ProductVariant.MaxSkuLength);
        builder.Property(variant => variant.Ean).HasMaxLength(14);
        builder.Property(variant => variant.OptionValues).HasColumnName("option_values");

        // A SKU is what a warehouse and an invoice call one thing, so it is one thing across the whole company.
        builder.HasIndex(variant => new { variant.TenantId, variant.Sku }).IsUnique();
        builder.HasIndex(variant => variant.ProductId);
    }
}

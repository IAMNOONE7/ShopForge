using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Catalog.Domain;
using ShopForge.Catalog.Feeds;

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

internal sealed class StoreFeedEntityConfiguration : IEntityTypeConfiguration<StoreFeed>
{
    public void Configure(EntityTypeBuilder<StoreFeed> builder)
    {
        builder.ToTable("store_feeds", CatalogModule.Schema);

        builder.Property(feed => feed.Feed).HasMaxLength(30);
        builder.Property(feed => feed.Token).HasMaxLength(100);
        builder.Property(feed => feed.FilePath).HasMaxLength(300);
        builder.Property(feed => feed.LastError).HasMaxLength(500);

        // One arrangement per shop per engine, and the token is how a collection is answered, so it has to
        // find its row without a store in hand.
        builder.HasIndex(feed => new { feed.StoreId, feed.Feed }).IsUnique();
        builder.HasIndex(feed => feed.Token).IsUnique();
    }
}

internal sealed class CategoryFeedMappingEntityConfiguration : IEntityTypeConfiguration<CategoryFeedMapping>
{
    public void Configure(EntityTypeBuilder<CategoryFeedMapping> builder)
    {
        builder.ToTable("category_feed_mappings", CatalogModule.Schema);

        builder.Property(mapping => mapping.Feed).HasMaxLength(30);
        builder.Property(mapping => mapping.EngineCategory).HasMaxLength(CategoryFeedMapping.MaxCategoryLength);

        // One answer per category per engine.
        builder.HasIndex(mapping => new { mapping.StoreId, mapping.CategoryId, mapping.Feed }).IsUnique();
    }
}

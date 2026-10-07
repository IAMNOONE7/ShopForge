using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Shared.Stores;
using ShopForge.Stores.Content;

namespace ShopForge.Stores.Persistence;

internal sealed class ContentPageEntityConfiguration : IEntityTypeConfiguration<ContentPage>
{
    public void Configure(EntityTypeBuilder<ContentPage> builder)
    {
        builder.ToTable("content_pages", StoresModule.Schema);

        builder.Property(page => page.Slug).HasMaxLength(Slugs.MaxLength);
        builder.Property(page => page.Title).HasMaxLength(ContentPage.MaxTitleLength);
        builder.Property(page => page.Body).HasMaxLength(ContentPage.MaxBodyLength);
        builder.Property(page => page.SeoTitle).HasMaxLength(200);
        builder.Property(page => page.SeoDescription).HasMaxLength(500);

        // One page per address within a shop. Two shops may both have a page called "terms", which is the
        // point of the store being in the key.
        builder.HasIndex(page => new { page.StoreId, page.Slug }).IsUnique();
    }
}

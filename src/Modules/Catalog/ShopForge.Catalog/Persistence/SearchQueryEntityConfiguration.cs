using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Catalog.Search;

namespace ShopForge.Catalog.Persistence;

internal sealed class SearchQueryEntityConfiguration : IEntityTypeConfiguration<SearchQuery>
{
    public void Configure(EntityTypeBuilder<SearchQuery> builder)
    {
        builder.ToTable("search_queries", CatalogModule.Schema);

        builder.Property(query => query.Terms).HasMaxLength(SearchTerms.Longest);

        // What Stage 33 will read: the most asked, and the most asked that found nothing.
        builder.HasIndex(query => new { query.StoreId, query.AskedAt });
    }
}

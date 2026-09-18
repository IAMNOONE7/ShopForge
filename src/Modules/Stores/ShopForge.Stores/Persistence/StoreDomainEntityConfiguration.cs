using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Persistence;

internal sealed class StoreDomainEntityConfiguration : IEntityTypeConfiguration<StoreDomain>
{
    public void Configure(EntityTypeBuilder<StoreDomain> builder)
    {
        builder.ToTable("store_domains", StoresModule.Schema, table =>
            table.HasCheckConstraint("ck_store_domains_host_name_normalized", "host_name = lower(host_name) AND host_name !~ '[:/\\s]'"));

        builder.Property(domain => domain.HostName).HasMaxLength(253);

        builder.HasIndex(domain => domain.HostName).IsUnique();
        builder.HasIndex(domain => domain.StoreId);
        builder.HasIndex(domain => domain.StoreId, "ix_store_domains_store_id_primary")
            .IsUnique()
            .HasFilter("is_primary")
            .HasDatabaseName("ix_store_domains_store_id_primary");
    }
}

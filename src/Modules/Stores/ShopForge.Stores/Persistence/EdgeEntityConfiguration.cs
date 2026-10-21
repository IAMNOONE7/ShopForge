using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Persistence;

internal sealed class DomainBindingEntityConfiguration : IEntityTypeConfiguration<DomainBinding>
{
    public void Configure(EntityTypeBuilder<DomainBinding> builder)
    {
        builder.ToTable("domain_bindings", StoresModule.Schema);

        builder.Property(binding => binding.HostName).HasMaxLength(253);
        builder.Property(binding => binding.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(binding => binding.ZoneId).HasMaxLength(64);
        builder.Property(binding => binding.ResourceName).HasMaxLength(100);
        builder.Property(binding => binding.Validation).HasConversion<string>().HasMaxLength(40);
        builder.Property(binding => binding.ValidationToken).HasMaxLength(200);

        // A host is on the edge for one store at a time, across the platform (D-212).
        builder.HasIndex(binding => binding.HostName).IsUnique();
        builder.HasIndex(binding => binding.StoreId);
    }
}

internal sealed class StoreZoneEntityConfiguration : IEntityTypeConfiguration<StoreZone>
{
    public void Configure(EntityTypeBuilder<StoreZone> builder)
    {
        builder.ToTable("store_zones", StoresModule.Schema);

        builder.Property(zone => zone.Name).HasMaxLength(StoreZone.MaxNameLength);
        builder.Property(zone => zone.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(zone => zone.ProviderZoneId).HasMaxLength(64);

        // A zone is one store's across the platform: the friendly refusal reads first, and this catches two at once.
        builder.HasIndex(zone => zone.Name).IsUnique();
        builder.HasIndex(zone => zone.StoreId);
    }
}

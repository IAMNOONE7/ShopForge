using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Shared.Email;

namespace ShopForge.Infrastructure.Email;

internal sealed class SuppressedAddressEntityConfiguration : IEntityTypeConfiguration<SuppressedAddress>
{
    public void Configure(EntityTypeBuilder<SuppressedAddress> builder)
    {
        builder.ToTable("suppressed_addresses", "messaging");

        builder.Property(address => address.Email).HasMaxLength(Emails.MaxLength);
        builder.Property(address => address.Reason).HasConversion<string>().HasMaxLength(20);
        builder.Property(address => address.Detail).HasMaxLength(500);

        // One row per address per store: a second bounce updates what is there rather than adding to it.
        builder.HasIndex(address => new { address.StoreId, address.Email }).IsUnique();
    }
}

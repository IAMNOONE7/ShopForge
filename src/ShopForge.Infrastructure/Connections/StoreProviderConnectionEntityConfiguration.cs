using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Shared.Connections;
using ShopForge.Shared.Security;

namespace ShopForge.Infrastructure.Connections;

internal sealed class StoreProviderConnectionEntityConfiguration : IEntityTypeConfiguration<StoreProviderConnection>
{
    public void Configure(EntityTypeBuilder<StoreProviderConnection> builder)
    {
        builder.ToTable("provider_connections", "connections");

        builder.Property(connection => connection.Provider).HasMaxLength(50);
        builder.Property(connection => connection.MerchantId).HasMaxLength(StoreProviderConnection.MaxMerchantIdLength);
        builder.Property(connection => connection.SecretName).HasMaxLength(SecretNames.MaxLength);
        builder.Property(connection => connection.Environment).HasConversion<string>().HasMaxLength(10);

        // One connection per provider per store: "which merchant does this storefront take money through" has a
        // single answer, and a second row would make it ambiguous at the moment money moves.
        builder.HasIndex(connection => new { connection.StoreId, connection.Provider }).IsUnique();
    }
}

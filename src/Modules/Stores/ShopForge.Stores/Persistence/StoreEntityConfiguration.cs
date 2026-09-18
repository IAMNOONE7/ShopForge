using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Persistence;

internal sealed class StoreEntityConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("stores", StoresModule.Schema, table =>
            table.HasCheckConstraint("ck_stores_currency", "currency ~ '^[A-Z]{3}$'"));

        builder.Property(store => store.Name).HasMaxLength(200);
        builder.Property(store => store.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(store => store.Culture).HasMaxLength(35);
        builder.ComplexProperty(store => store.Theme, theme => theme.ToJson());

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(store => store.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(store => store.Domains)
            .WithOne()
            .HasForeignKey(domain => domain.StoreId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

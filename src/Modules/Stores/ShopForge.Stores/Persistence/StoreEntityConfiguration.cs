using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Persistence;

internal sealed class StoreEntityConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("stores", StoresModule.Schema, table =>
        {
            table.HasCheckConstraint("ck_stores_currency", "currency ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("ck_stores_return_window_days", "return_window_days >= 0");
        });

        builder.Property(store => store.Name).HasMaxLength(200);
        builder.Property(store => store.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(store => store.Culture).HasMaxLength(35);
        builder.Property(store => store.LogoPath).HasMaxLength(300);
        builder.Property(store => store.ReturnWindowDays).HasDefaultValue(Domain.Store.DefaultReturnWindowDays);
        builder.Property(store => store.Status).HasConversion<string>().HasMaxLength(20);
        builder.ComplexProperty(store => store.Theme, theme => theme.ToJson());

        builder.OwnsOne(store => store.Company, company =>
        {
            company.Property(value => value.LegalName).HasMaxLength(200).HasColumnName("company_legal_name");
            company.Property(value => value.Line1).HasMaxLength(200).HasColumnName("company_line1");
            company.Property(value => value.City).HasMaxLength(100).HasColumnName("company_city");
            company.Property(value => value.PostalCode).HasMaxLength(20).HasColumnName("company_postal_code");
            company.Property(value => value.Country).HasMaxLength(2).IsFixedLength().HasColumnName("company_country");
            company.Property(value => value.RegistrationNumber).HasMaxLength(50).HasColumnName("company_registration_number");
            company.Property(value => value.VatNumber).HasMaxLength(50).HasColumnName("company_vat_number");
        });

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

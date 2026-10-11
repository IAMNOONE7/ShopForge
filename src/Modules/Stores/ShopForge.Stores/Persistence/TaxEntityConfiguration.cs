using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Persistence;

internal sealed class TaxClassEntityConfiguration : IEntityTypeConfiguration<TaxClass>
{
    public void Configure(EntityTypeBuilder<TaxClass> builder)
    {
        builder.ToTable("tax_classes", StoresModule.Schema);

        builder.Property(taxClass => taxClass.Code).HasMaxLength(TaxClass.MaxCodeLength);
        builder.Property(taxClass => taxClass.Name).HasMaxLength(TaxClass.MaxNameLength);

        // One class per code within a shop; two shops may both call something "reduced".
        builder.HasIndex(taxClass => new { taxClass.StoreId, taxClass.Code }).IsUnique();
    }
}

internal sealed class TaxRateEntityConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> builder)
    {
        builder.ToTable("tax_rates", StoresModule.Schema, table =>
            table.HasCheckConstraint("ck_tax_rates_rate", "rate >= 0 AND rate <= 100"));

        builder.Property(rate => rate.TaxClass).HasMaxLength(TaxClass.MaxCodeLength);
        builder.Property(rate => rate.Country).HasMaxLength(2).IsFixedLength();
        builder.Property(rate => rate.Rate).HasPrecision(5, 2);

        // One rate per class per country: a second row for the same pair would be two answers to one question.
        builder.HasIndex(rate => new { rate.StoreId, rate.TaxClass, rate.Country }).IsUnique();
    }
}

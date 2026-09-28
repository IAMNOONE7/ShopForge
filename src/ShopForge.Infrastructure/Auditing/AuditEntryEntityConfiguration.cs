using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShopForge.Infrastructure.Auditing;

internal sealed class AuditEntryEntityConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries", "auditing");

        builder.Property(entry => entry.ActorKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(entry => entry.ActorName).HasMaxLength(254);
        builder.Property(entry => entry.Action).HasMaxLength(60);
        builder.Property(entry => entry.Subject).HasMaxLength(200);
        builder.Property(entry => entry.Details).HasMaxLength(AuditEntry.MaxDetailsLength);
        builder.Property(entry => entry.IpAddress).HasMaxLength(45);

        // Reading is always "the newest entries of this company", narrowed from there.
        builder.HasIndex(entry => new { entry.TenantId, entry.RecordedAt });
        builder.HasIndex(entry => new { entry.StoreId, entry.RecordedAt });
    }
}

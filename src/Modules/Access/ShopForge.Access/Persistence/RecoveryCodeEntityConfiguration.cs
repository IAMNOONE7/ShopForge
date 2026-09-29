using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Access.Domain;

namespace ShopForge.Access.Persistence;

internal sealed class RecoveryCodeEntityConfiguration : IEntityTypeConfiguration<RecoveryCode>
{
    public void Configure(EntityTypeBuilder<RecoveryCode> builder)
    {
        builder.ToTable("recovery_codes", AccessModule.Schema);

        builder.Property(code => code.CodeHash).HasMaxLength(64);

        builder.HasIndex(code => code.TenantUserId);
        builder.HasIndex(code => code.CodeHash).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Access.Domain;

namespace ShopForge.Access.Persistence;

internal sealed class TenantUserEntityConfiguration : IEntityTypeConfiguration<TenantUser>
{
    public void Configure(EntityTypeBuilder<TenantUser> builder)
    {
        builder.ToTable("tenant_users", AccessModule.Schema, table =>
            table.HasCheckConstraint("ck_tenant_users_email_normalized", "email = lower(btrim(email))"));

        builder.Property(user => user.Email).HasMaxLength(254);
        builder.Property(user => user.PasswordHash).HasMaxLength(200);
        builder.Property(user => user.Role).HasConversion<string>().HasMaxLength(32);
        builder.Property(user => user.TwoFactorSecret).HasMaxLength(64);

        builder.HasIndex(user => user.Email).IsUnique();
        builder.HasIndex(user => user.TenantId);
    }
}

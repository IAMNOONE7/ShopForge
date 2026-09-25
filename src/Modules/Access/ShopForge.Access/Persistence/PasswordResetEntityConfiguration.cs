using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Access.Domain;

namespace ShopForge.Access.Persistence;

internal sealed class PasswordResetEntityConfiguration : IEntityTypeConfiguration<PasswordReset>
{
    public void Configure(EntityTypeBuilder<PasswordReset> builder)
    {
        builder.ToTable("password_resets", AccessModule.Schema);

        builder.Property(reset => reset.TokenHash).HasMaxLength(64);

        builder.HasIndex(reset => reset.TokenHash).IsUnique();
        builder.HasIndex(reset => reset.TenantUserId);
    }
}

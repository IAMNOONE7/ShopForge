using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Platform.Domain;
using ShopForge.Shared.Email;

namespace ShopForge.Platform.Persistence;

internal sealed class PlatformUserEntityConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> builder)
    {
        builder.ToTable("platform_users", PlatformModule.Schema);

        builder.Property(user => user.Email).HasMaxLength(Emails.MaxLength);
        builder.Property(user => user.PasswordHash).HasMaxLength(200);
        builder.Property(user => user.TwoFactorSecret).HasMaxLength(64);

        builder.HasIndex(user => user.Email).IsUnique();
    }
}

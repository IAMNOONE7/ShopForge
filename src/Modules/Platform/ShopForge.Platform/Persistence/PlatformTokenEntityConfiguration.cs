using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Platform.Domain;
using ShopForge.Shared.Email;

namespace ShopForge.Platform.Persistence;

internal sealed class PlatformInvitationEntityConfiguration : IEntityTypeConfiguration<PlatformInvitation>
{
    public void Configure(EntityTypeBuilder<PlatformInvitation> builder)
    {
        builder.ToTable("platform_invitations", PlatformModule.Schema);

        builder.Property(invitation => invitation.Email).HasMaxLength(Emails.MaxLength);
        builder.Property(invitation => invitation.TokenHash).HasMaxLength(64);

        builder.HasIndex(invitation => invitation.TokenHash).IsUnique();
        builder.HasIndex(invitation => invitation.Email);
    }
}

internal sealed class PlatformPasswordResetEntityConfiguration : IEntityTypeConfiguration<PlatformPasswordReset>
{
    public void Configure(EntityTypeBuilder<PlatformPasswordReset> builder)
    {
        builder.ToTable("platform_password_resets", PlatformModule.Schema);

        builder.Property(reset => reset.TokenHash).HasMaxLength(64);

        builder.HasIndex(reset => reset.TokenHash).IsUnique();
        builder.HasIndex(reset => reset.PlatformUserId);
    }
}

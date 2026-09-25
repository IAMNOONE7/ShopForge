using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Access.Domain;

namespace ShopForge.Access.Persistence;

internal sealed class TenantInvitationEntityConfiguration : IEntityTypeConfiguration<TenantInvitation>
{
    public void Configure(EntityTypeBuilder<TenantInvitation> builder)
    {
        builder.ToTable("tenant_invitations", AccessModule.Schema, table =>
            table.HasCheckConstraint("ck_tenant_invitations_email_normalized", "email = lower(btrim(email))"));

        builder.Property(invitation => invitation.Email).HasMaxLength(254);
        builder.Property(invitation => invitation.Role).HasConversion<string>().HasMaxLength(32);
        builder.Property(invitation => invitation.TokenHash).HasMaxLength(64);

        builder.HasIndex(invitation => invitation.TokenHash).IsUnique();
        builder.HasIndex(invitation => new { invitation.TenantId, invitation.Email });
    }
}

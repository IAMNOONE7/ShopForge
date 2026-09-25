using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Domain;

// How somebody becomes staff of a company: an invitation carries the role they are being given and a link only the
// person holding the mailbox can follow. Nobody types a colleague's password for them (D-112).
internal sealed class TenantInvitation : ITenantOwned
{
    private TenantInvitation()
    {
    }

    public TenantInvitation(Guid tenantId, string email, TenantRole role, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Email = TenantUser.NormalizeEmail(email);
        Role = role;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Email { get; private set; } = null!;

    public TenantRole Role { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => AcceptedAt is null && now < ExpiresAt;

    public void Accept(DateTimeOffset now) => AcceptedAt = now;
}

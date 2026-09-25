using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Domain;

// A way back in for somebody who works here and has forgotten their password. Like every link ShopForge sends, only
// the hash is kept, it expires, and it works once (D-113).
internal sealed class PasswordReset : ITenantOwned
{
    private PasswordReset()
    {
    }

    public PasswordReset(Guid tenantId, Guid tenantUserId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        TenantUserId = tenantUserId;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid TenantUserId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

    public void Use(DateTimeOffset now) => UsedAt = now;
}

using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Domain;

// The way back in when the phone is gone. Hashed like any other credential, used once, and handed over exactly
// once at enrolment — a second factor nobody can recover from is a way to lose a company (D-128).
internal sealed class RecoveryCode : ITenantOwned
{
    public const int Count = 10;

    private RecoveryCode()
    {
    }

    public RecoveryCode(Guid tenantId, Guid tenantUserId, string codeHash)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        TenantUserId = tenantUserId;
        CodeHash = codeHash;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid TenantUserId { get; private set; }

    public string CodeHash { get; private set; } = null!;

    public DateTimeOffset? UsedAt { get; private set; }

    public void Use(DateTimeOffset now) => UsedAt = now;
}

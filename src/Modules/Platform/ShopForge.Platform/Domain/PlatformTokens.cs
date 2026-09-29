namespace ShopForge.Platform.Domain;

// An invitation to run ShopForge, and a way back in for an operator who has forgotten their password. Neither
// belongs to a company — that is what being the platform means (D-103) — so neither is tenancy-owned.
internal sealed class PlatformInvitation
{
    private PlatformInvitation()
    {
    }

    public PlatformInvitation(string email, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        Id = Guid.CreateVersion7();
        Email = PlatformUser.NormalizeEmail(email);
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = null!;

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => AcceptedAt is null && now < ExpiresAt;

    public void Accept(DateTimeOffset now) => AcceptedAt = now;
}

internal sealed class PlatformPasswordReset
{
    private PlatformPasswordReset()
    {
    }

    public PlatformPasswordReset(Guid platformUserId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7();
        PlatformUserId = platformUserId;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid PlatformUserId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

    public void Use(DateTimeOffset now) => UsedAt = now;
}

// The way back in when an operator loses their phone. Nothing here belongs to a company, so unlike the staff
// version it carries no tenant (D-103, D-129).
internal sealed class PlatformRecoveryCode
{
    private PlatformRecoveryCode()
    {
    }

    public PlatformRecoveryCode(Guid platformUserId, string codeHash)
    {
        Id = Guid.CreateVersion7();
        PlatformUserId = platformUserId;
        CodeHash = codeHash;
    }

    public Guid Id { get; private set; }

    public Guid PlatformUserId { get; private set; }

    public string CodeHash { get; private set; } = null!;

    public DateTimeOffset? UsedAt { get; private set; }

    public void Use(DateTimeOffset now) => UsedAt = now;
}

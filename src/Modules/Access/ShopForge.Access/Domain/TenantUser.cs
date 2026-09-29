using ShopForge.Shared.Tenancy;

namespace ShopForge.Access.Domain;

internal sealed class TenantUser : ITenantOwned
{
    private TenantUser()
    {
    }

    public TenantUser(Guid tenantId, string email, TenantRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Email = NormalizeEmail(email);
        Role = role;
        IsActive = true;
        SecurityStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Email { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public TenantRole Role { get; private set; }

    public bool IsActive { get; private set; }

    // Changed whenever every session of theirs should end: a new password, or asking to be signed out everywhere.
    public Guid SecurityStamp { get; private set; }

    // Held in the clear because verifying a code means computing it, which a one-way hash cannot do. The database
    // is the trust boundary for it, as it is for the shop's money (D-128).
    public string? TwoFactorSecret { get; private set; }

    // Set only once a code has been typed back, so an enrolment somebody abandoned halfway cannot lock them out.
    public DateTimeOffset? TwoFactorEnabledAt { get; private set; }

    public bool IsTwoFactorEnabled => TwoFactorEnabledAt is not null;

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void EndEverySession() => SecurityStamp = Guid.CreateVersion7();

    public void BeginTwoFactor(string secret)
    {
        TwoFactorSecret = secret;
        TwoFactorEnabledAt = null;
    }

    public void ConfirmTwoFactor(DateTimeOffset now) => TwoFactorEnabledAt = now;

    public void TurnOffTwoFactor()
    {
        TwoFactorSecret = null;
        TwoFactorEnabledAt = null;
    }

    public void ChangeRole(TenantRole role) => Role = role;

    public void SetActive(bool isActive) => IsActive = isActive;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

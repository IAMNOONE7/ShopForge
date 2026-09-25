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

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void EndEverySession() => SecurityStamp = Guid.CreateVersion7();

    public void ChangeRole(TenantRole role) => Role = role;

    public void SetActive(bool isActive) => IsActive = isActive;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

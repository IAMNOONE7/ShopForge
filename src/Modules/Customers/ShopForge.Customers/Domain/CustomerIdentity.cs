using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Domain;

internal sealed class CustomerIdentity : ITenantOwned
{
    private CustomerIdentity()
    {
    }

    public CustomerIdentity(Guid tenantId, string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Email = NormalizeEmail(email);
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Email { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public bool IsEmailVerified { get; private set; }

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void VerifyEmail() => IsEmailVerified = true;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

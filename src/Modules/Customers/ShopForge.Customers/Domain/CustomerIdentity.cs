using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Domain;

// Who the person is inside the company: one row per e-mail address, shared by their relationships with the
// company's stores. It says nothing about how they sign in — a password and a proved address belong to the store
// the customer has them with (D-102).
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

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

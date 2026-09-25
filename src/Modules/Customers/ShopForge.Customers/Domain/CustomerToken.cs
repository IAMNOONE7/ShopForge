using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Domain;

// Tokens travel by e-mail and are stored as hashes, so a leaked database row cannot be used to take over an account.
// They are owned by the store that sent them: a link from one store is not a link at another, and the query filter
// is what says so rather than a check somebody can forget (D-102).
internal sealed class CustomerToken : IStoreOwned, ITenantOwned
{
    private CustomerToken()
    {
    }

    public CustomerToken(Guid tenantId, Guid customerIdentityId, Guid storeId, CustomerTokenPurpose purpose, string tokenHash, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        CustomerIdentityId = customerIdentityId;
        StoreId = storeId;
        Purpose = purpose;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid CustomerIdentityId { get; private set; }

    // The store the link was sent from: verification creates the customer's relationship with that store.
    public Guid StoreId { get; private set; }

    public CustomerTokenPurpose Purpose { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

    public void Use(DateTimeOffset now) => UsedAt = now;
}

internal enum CustomerTokenPurpose
{
    EmailVerification,
    PasswordReset,
}

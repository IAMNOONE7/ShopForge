using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Domain;

// An address a customer has asked to move to, proved the same way the first one was: the link goes to the new
// address and nothing changes until it is followed. It belongs to the store the customer asked at, because that is
// the relationship it moves (D-115).
internal sealed class EmailChange : IStoreOwned, ITenantOwned
{
    private EmailChange()
    {
    }

    public EmailChange(Guid tenantId, Guid storeId, Guid storeCustomerId, string newEmail, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newEmail);

        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        StoreId = storeId;
        StoreCustomerId = storeCustomerId;
        NewEmail = CustomerIdentity.NormalizeEmail(newEmail);
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid StoreCustomerId { get; private set; }

    public string NewEmail { get; private set; } = null!;

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

    public void Use(DateTimeOffset now) => UsedAt = now;
}

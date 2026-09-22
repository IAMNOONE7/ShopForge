using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Domain;

// Tokens travel by e-mail and are stored as hashes, so a leaked database row cannot be used to take over an account.
internal sealed class CustomerToken : ITenantOwned
{
    private CustomerToken()
    {
    }

    public CustomerToken(Guid tenantId, Guid customerIdentityId, Guid? storeId, CustomerTokenPurpose purpose, string tokenHash, DateTimeOffset expiresAt)
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
    public Guid? StoreId { get; private set; }

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

internal static class TokenValues
{
    public static (string Value, string Hash) Create()
    {
        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        return (value, Hash(value));
    }

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

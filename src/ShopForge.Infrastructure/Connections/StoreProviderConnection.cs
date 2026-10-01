using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Connections;

// A store's connection to one provider. The merchant id is what the provider calls the account this storefront
// was approved for; the secret name is where its credentials are kept, never the credentials themselves (D-139).
internal sealed class StoreProviderConnection : IStoreOwned
{
    public const int MaxMerchantIdLength = 100;

    private StoreProviderConnection()
    {
    }

    public StoreProviderConnection(Guid storeId, string provider, string merchantId, ProviderEnvironment environment, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Provider = provider.Trim().ToLowerInvariant();
        Update(merchantId, environment, isActive: true, createdAt);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Provider { get; private set; } = null!;

    public string MerchantId { get; private set; } = null!;

    public ProviderEnvironment Environment { get; private set; }

    // The name the credentials are filed under, not the credentials. Null until somebody sets them, which is
    // why a connection can exist and still not be usable.
    public string? SecretName { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public void Update(string merchantId, ProviderEnvironment environment, bool isActive, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantId);

        MerchantId = merchantId.Trim();
        Environment = environment;
        IsActive = isActive;
        ChangedAt = at;
    }

    public void KeepsSecretAt(string? secretName, DateTimeOffset at)
    {
        SecretName = secretName is null ? null : SecretNames.Required(secretName);
        ChangedAt = at;
    }
}

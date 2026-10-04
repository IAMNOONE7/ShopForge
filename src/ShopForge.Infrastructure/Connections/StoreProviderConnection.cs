using ShopForge.Shared.Connections;
using ShopForge.Shared.Payments;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Connections;

// A store's connection to one provider. The merchant id is what the provider calls the account this storefront
// was approved for; the secret name is where its credentials are kept, never the credentials themselves (D-139).
internal sealed class StoreProviderConnection : IStoreOwned
{
    public const int MaxMerchantIdLength = 100;
    public const int MaxPublishableKeyLength = 200;

    private StoreProviderConnection()
    {
    }

    public StoreProviderConnection(
        Guid storeId,
        string provider,
        string merchantId,
        ProviderEnvironment environment,
        DateTimeOffset createdAt,
        string? publishableKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        Id = Guid.CreateVersion7();
        StoreId = storeId;
        Provider = provider.Trim().ToLowerInvariant();
        Update(merchantId, environment, isActive: true, createdAt, publishableKey);
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Provider { get; private set; } = null!;

    public string MerchantId { get; private set; } = null!;

    public ProviderEnvironment Environment { get; private set; }

    // The name the credentials are filed under, not the credentials. Null until somebody sets them, which is
    // why a connection can exist and still not be usable.
    public string? SecretName { get; private set; }

    // The half of a provider's credentials that is meant to be seen: a key the shopper's own page sends to the
    // provider, like a carrier's map widget or a gateway's browser script. It is kept beside the secret's name
    // and never confused with it — this one is published on purpose, that one can never come out at all.
    public string? PublishableKey { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public void Update(string merchantId, ProviderEnvironment environment, bool isActive, DateTimeOffset at, string? publishableKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(merchantId);

        MerchantId = merchantId.Trim();
        Environment = environment;
        IsActive = isActive;
        PublishableKey = string.IsNullOrWhiteSpace(publishableKey) ? null : publishableKey.Trim();
        ChangedAt = at;
    }

    public void KeepsSecretAt(string? secretName, DateTimeOffset at)
    {
        SecretName = secretName is null ? null : SecretNames.Required(secretName);
        ChangedAt = at;
    }
}

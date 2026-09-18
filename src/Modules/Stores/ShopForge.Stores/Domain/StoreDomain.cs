using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Domain;

internal sealed class StoreDomain : IStoreOwned
{
    private StoreDomain()
    {
    }

    internal StoreDomain(Guid storeId, string hostName, bool isPrimary)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        HostName = HostNames.Normalize(hostName)
            ?? throw new ArgumentException($"'{hostName}' is not a valid host name.", nameof(hostName));
        IsPrimary = isPrimary;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string HostName { get; private set; } = null!;

    public bool IsPrimary { get; private set; }
}

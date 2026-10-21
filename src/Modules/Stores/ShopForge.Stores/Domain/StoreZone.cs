using ShopForge.Shared.Dns;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Domain;

// A domain whose DNS the platform runs for a store (D-212). Made when the merchant asks, waiting until the domain's
// nameservers point at it, and from then on where the platform publishes that domain's records itself: the proof of a
// claim under it, and everything the edge needs. A zone is one store's across the platform.
internal sealed class StoreZone : IStoreOwned
{
    public const int MaxNameLength = 253;

    private StoreZone()
    {
    }

    public StoreZone(Guid tenantId, Guid storeId, string name, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        StoreId = storeId;
        Name = name;
        State = StoreZoneState.Waiting;
        CreatedAt = now;
        ChangedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public string Name { get; private set; } = null!;

    public StoreZoneState State { get; private set; }

    public Guid? OperationId { get; private set; }

    public string? ProviderZoneId { get; private set; }

    // Where the merchant points the domain at their registrar.
    public string[] NameServers { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    // Whether a host name is this zone's: the zone's own name, or one under it.
    public bool Holds(string hostName) =>
        hostName == Name || hostName.EndsWith($".{Name}", StringComparison.Ordinal);

    public void Begin(Guid operationId, DateTimeOffset now)
    {
        OperationId = operationId;
        ChangedAt = now;
    }

    public void Record(DnsZone zone, DateTimeOffset now)
    {
        ProviderZoneId = zone.Id;
        NameServers = [.. zone.NameServers];
        ChangedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        State = StoreZoneState.Active;
        ActivatedAt ??= now;
        ChangedAt = now;
    }
}

internal enum StoreZoneState
{
    Waiting,
    Active,
}

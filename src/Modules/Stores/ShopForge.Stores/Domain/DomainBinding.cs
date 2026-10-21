using ShopForge.Shared.Edge;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Domain;

// A store's proved domain at the edge, and how far binding it has got (D-212). Keyed by the host name rather than by the
// store's domain, because a domain the store has let go of is still being taken off the edge afterwards.
internal sealed class DomainBinding : IStoreOwned
{
    private DomainBinding()
    {
    }

    public DomainBinding(Guid tenantId, Guid storeId, string hostName, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        StoreId = storeId;
        HostName = hostName;
        State = DomainBindingState.Binding;
        CreatedAt = now;
        ChangedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid StoreId { get; private set; }

    public string HostName { get; private set; } = null!;

    public DomainBindingState State { get; private set; }

    public Guid? OperationId { get; private set; }

    // The zone the platform runs that holds the domain, when there is one: then the platform publishes its records, and
    // otherwise the merchant does.
    public string? ZoneId { get; private set; }

    public string? ResourceName { get; private set; }

    // What the edge last said: how far the proof has got, and the token it looks for at _dnsauth.<host>. The token is
    // published in DNS for anyone to read, so it is no secret.
    public EdgeValidation? Validation { get; private set; }

    public string? ValidationToken { get; private set; }

    public DateTimeOffset? ValidationTokenExpires { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public DateTimeOffset? BoundAt { get; private set; }

    public DateTimeOffset? CheckedAt { get; private set; }

    public bool RecordsByPlatform => ZoneId is not null;

    public void Begin(Guid operationId, DateTimeOffset now)
    {
        OperationId = operationId;
        ChangedAt = now;
    }

    public void UseZone(string? zoneId, DateTimeOffset now)
    {
        ZoneId = zoneId;
        ChangedAt = now;
    }

    public void Record(EdgeDomain domain, DateTimeOffset now)
    {
        ResourceName = domain.ResourceName;
        Validation = domain.Validation;
        ValidationToken = domain.ValidationToken;
        ValidationTokenExpires = domain.ValidationTokenExpires;
        CheckedAt = now;
    }

    public void Bind(DateTimeOffset now)
    {
        State = DomainBindingState.Bound;
        BoundAt ??= now;
        ChangedAt = now;
    }

    public void Unbind(DateTimeOffset now)
    {
        State = DomainBindingState.Unbinding;
        ChangedAt = now;
    }
}

internal enum DomainBindingState
{
    Binding,
    Bound,
    Unbinding,
}

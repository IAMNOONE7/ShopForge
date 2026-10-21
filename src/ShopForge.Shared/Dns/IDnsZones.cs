namespace ShopForge.Shared.Dns;

// The DNS zones the platform runs, at a provider with an API (D-212): its own, and a merchant's whose nameservers point
// at the platform's account. Records written here are marked as the platform's in their comment.
public interface IDnsZones
{
    // Whether this deployment runs any zone. Without it, a merchant publishes every record by hand.
    bool IsConfigured { get; }

    Task<DnsZone?> FindAsync(string name, CancellationToken cancellationToken);

    Task<DnsZone> CreateAsync(string name, CancellationToken cancellationToken);

    // Null once the zone is gone.
    Task<DnsZone?> ZoneAsync(string zoneId, CancellationToken cancellationToken);

    // Asks the provider to look at the delegation now rather than whenever it next would. A zone on the free plan may
    // ask once an hour.
    Task CheckDelegationAsync(string zoneId, CancellationToken cancellationToken);

    Task DeleteAsync(string zoneId, CancellationToken cancellationToken);

    // The active zone of the platform's that holds a host name, the longest name winning; null when none does.
    Task<DnsZone?> HoldingAsync(string hostName, CancellationToken cancellationToken);

    // Makes the zone answer exactly this value for this name and type. A record already right is left alone, one with
    // another value is changed, and any other of the same name and type is removed. Answers whether it wrote.
    Task<DnsRecordChange> EnsureAsync(string zoneId, DnsRecordSpec record, CancellationToken cancellationToken);

    // Every record of this name and type, or only those with this value; answers how many went.
    Task<int> RemoveAsync(string zoneId, string name, string type, string? value, CancellationToken cancellationToken);

    Task<IReadOnlyList<DnsRecord>> RecordsAsync(string zoneId, string name, string type, CancellationToken cancellationToken);
}

public sealed record DnsZone(string Id, string Name, DnsZoneStatus Status, IReadOnlyList<string> NameServers);

public enum DnsZoneStatus
{
    // Made, and waiting for the domain's nameservers to point at it.
    Pending,
    Active,

    // The domain's nameservers point somewhere else again.
    Moved,
}

// TXT values are written and compared as the text itself, without the quotes DNS carries them in.
public sealed record DnsRecordSpec(string Name, string Type, string Value, int Ttl, string Comment);

public sealed record DnsRecord(string Id, string Name, string Type, string Value, string? Comment);

public sealed record DnsRecordChange(DnsRecord Record, bool Written);

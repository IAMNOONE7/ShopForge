using ShopForge.Shared.Dns;

namespace ShopForge.Infrastructure.Dns;

// A deployment that runs no zone: every record is the merchant's to publish, and no domain is held by the platform.
internal sealed class NoDnsZones : IDnsZones
{
    public bool IsConfigured => false;

    public Task<DnsZone?> FindAsync(string name, CancellationToken cancellationToken) => Task.FromResult<DnsZone?>(null);

    public Task<DnsZone> CreateAsync(string name, CancellationToken cancellationToken) => throw NotConfigured();

    public Task<DnsZone?> ZoneAsync(string zoneId, CancellationToken cancellationToken) => Task.FromResult<DnsZone?>(null);

    public Task CheckDelegationAsync(string zoneId, CancellationToken cancellationToken) => throw NotConfigured();

    public Task DeleteAsync(string zoneId, CancellationToken cancellationToken) => throw NotConfigured();

    public Task<DnsZone?> HoldingAsync(string hostName, CancellationToken cancellationToken) => Task.FromResult<DnsZone?>(null);

    public Task<DnsRecordChange> EnsureAsync(string zoneId, DnsRecordSpec record, CancellationToken cancellationToken) => throw NotConfigured();

    public Task<int> RemoveAsync(string zoneId, string name, string type, string? value, CancellationToken cancellationToken) => throw NotConfigured();

    public Task<IReadOnlyList<DnsRecord>> RecordsAsync(string zoneId, string name, string type, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DnsRecord>>([]);

    private static InvalidOperationException NotConfigured() => new("This deployment runs no DNS zone: Dns:Cloudflare is not configured.");
}

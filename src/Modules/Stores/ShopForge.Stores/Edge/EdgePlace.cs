using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Edge;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Edge;

// What the admin shows of a store's domains at the edge (D-212): how far each has got, the zone the platform runs for
// it, and, where it does not, the two records the merchant publishes: the edge's token, while the edge wants it, and the
// record that sends traffic.
internal sealed class EdgePlace(IReadOnlyList<DomainBinding> bindings, IReadOnlyList<StoreZone> zones, IEdgeDomains edge, PlatformHosts platformHosts)
{
    public static async Task<EdgePlace> ForStoreAsync(DbContext dbContext, IEdgeDomains edge, PlatformHosts platformHosts, CancellationToken cancellationToken) =>
        new(
            await dbContext.Set<DomainBinding>().AsNoTracking().ToListAsync(cancellationToken),
            await dbContext.Set<StoreZone>().AsNoTracking().ToListAsync(cancellationToken),
            edge,
            platformHosts);

    // Nothing for the platform's own subdomain, which the template's wildcard serves, nor where nothing is bound at the
    // edge at all.
    public EdgeView? EdgeOf(StoreDomain domain)
    {
        if (!edge.IsConfigured || platformHosts.Owns(domain.HostName))
        {
            return null;
        }

        if (bindings.FirstOrDefault(binding => binding.HostName == domain.HostName) is not { } binding)
        {
            return new EdgeView(domain.IsVerified ? EdgeState.Waiting : EdgeState.Unproved, null, RecordsByPlatform: false, null, []);
        }

        return new EdgeView(
            binding.State switch
            {
                DomainBindingState.Bound => EdgeState.Bound,
                DomainBindingState.Unbinding => EdgeState.Unbinding,
                _ => EdgeState.Binding,
            },
            binding.OperationId,
            binding.RecordsByPlatform,
            binding.Validation,
            binding.RecordsByPlatform ? [] : Instructions(binding));
    }

    public ZoneView? ZoneOf(StoreDomain domain) =>
        zones.Where(zone => zone.Holds(domain.HostName))
            .OrderByDescending(zone => zone.Name.Length)
            .Select(zone => new ZoneView(zone.Name, zone.State, zone.NameServers, zone.OperationId))
            .FirstOrDefault();

    private List<DnsInstruction> Instructions(DomainBinding binding)
    {
        var records = new List<DnsInstruction>();

        if (binding.Validation != EdgeValidation.Approved && binding.ValidationToken is { } token)
        {
            records.Add(new DnsInstruction("TXT", EdgeNames.ValidationRecord(binding.HostName), token, "validation"));
        }

        // At the apex, where DNS allows no CNAME, a provider that flattens one or offers an ALIAS record.
        records.Add(new DnsInstruction("CNAME", binding.HostName, edge.TrafficTarget, "traffic"));

        return records;
    }
}

internal enum EdgeState
{
    // A claim not proved yet: nothing goes to the edge before the proof.
    Unproved,

    // Proved, and the binding not started yet: the pass starts it within a quarter of an hour.
    Waiting,
    Binding,
    Bound,
    Unbinding,
}

internal sealed record EdgeView(EdgeState State, Guid? OperationId, bool RecordsByPlatform, EdgeValidation? Validation, IReadOnlyList<DnsInstruction> Records);

internal sealed record DnsInstruction(string Type, string Name, string Value, string Purpose);

internal sealed record ZoneView(string Name, StoreZoneState State, IReadOnlyList<string> NameServers, Guid? OperationId);

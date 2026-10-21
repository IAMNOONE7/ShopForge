using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Edge;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Provisioning;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Edge;

// Keeps the edge's proof of every domain current (D-212). Front Door renews a certificate by itself only for a domain
// whose CNAME points straight at it; an apex served through a flattened CNAME, and a wildcard, are proved again before
// each renewal, from 45 days ahead of it (Microsoft Learn, "Domains in Azure Front Door"). So:
// - a merchant's bound domain the edge no longer approves gets a revalidation, which republishes the token where the
//   platform runs the zone and tells the merchant otherwise;
// - the template's own domains, the admin's and the stores' wildcard, are kept here directly, in a zone the platform
//   runs: the token published while the edge wants it, and the traffic record pointed at the endpoint;
// - a merchant's domain at the edge that no store binds is reported for a person and never deleted here.
internal sealed class EdgeValidationsPass(IServiceProvider services, TimeProvider clock, ILogger<EdgeValidationsPass> logger) : IScheduledPass
{
    public const string PlatformComment = "ShopForge, the platform's own domain";

    public string Name => "edge-validations";

    public TimeSpan Period => TimeSpan.FromHours(6);

    Task IScheduledPass.RunAsync(CancellationToken cancellationToken) => RunAsync(cancellationToken);

    // How many revalidations it started.
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var edge = scope.ServiceProvider.GetRequiredService<IEdgeDomains>();

        if (!edge.IsConfigured)
        {
            return 0;
        }

        var zones = scope.ServiceProvider.GetRequiredService<IDnsZones>();
        var domains = await edge.ListAsync(cancellationToken);
        var bindings = await scope.ServiceProvider.GetRequiredService<DbContext>().Set<DomainBinding>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var seen = new List<(DomainBinding Binding, EdgeDomain Domain)>();

        foreach (var domain in domains)
        {
            if (!domain.IsMerchants)
            {
                await KeepPlatformDomainAsync(domain, edge, zones, cancellationToken);
            }
            else if (bindings.FirstOrDefault(binding => string.Equals(binding.HostName, domain.HostName, StringComparison.OrdinalIgnoreCase)) is { } binding)
            {
                seen.Add((binding, domain));
            }
            else
            {
                logger.LogWarning("Front Door holds {HostName} ({ResourceName}), which no store binds.", domain.HostName, domain.ResourceName);
            }
        }

        var started = 0;

        foreach (var store in seen.GroupBy(pair => (pair.Binding.StoreId, pair.Binding.TenantId)))
        {
            started += await KeepStoreAsync(store.Key.StoreId, store.Key.TenantId, [.. store.Select(pair => pair.Domain)], cancellationToken);
        }

        return started;
    }

    // What the edge said of each, written down; a bound domain it no longer approves, revalidated.
    private async Task<int> KeepStoreAsync(Guid storeId, Guid tenantId, IReadOnlyList<EdgeDomain> domains, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(storeId, tenantId);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var provisioning = scope.ServiceProvider.GetRequiredService<IProvisioning>();
        var now = clock.GetUtcNow();
        var started = 0;

        foreach (var domain in domains)
        {
            if (await EdgeOperations.BindingAsync(dbContext, domain.HostName, cancellationToken) is not { } binding)
            {
                continue;
            }

            binding.Record(domain, now);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (binding.State == DomainBindingState.Bound && domain.Validation != EdgeValidation.Approved
                && await provisioning.StartAsync(EdgeOperations.Revalidate, binding.HostName, input: null, cancellationToken) is { Started: true } revalidation)
            {
                binding.Begin(revalidation.OperationId, now);
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Front Door wants {HostName} proved again; a revalidation was started.", binding.HostName);
                started++;
            }
        }

        return started;
    }

    // The template's domains are this profile's, so in a zone the platform runs they point at its endpoint, directly,
    // which is also what lets Front Door renew their certificates without asking.
    private async Task KeepPlatformDomainAsync(EdgeDomain domain, IEdgeDomains edge, IDnsZones zones, CancellationToken cancellationToken)
    {
        var name = domain.HostName.StartsWith("*.", StringComparison.Ordinal) ? domain.HostName[2..] : domain.HostName;

        if (await zones.HoldingAsync(name, cancellationToken) is not { } zone)
        {
            if (domain.Validation != EdgeValidation.Approved)
            {
                logger.LogWarning(
                    "{HostName} is not proved to Front Door ({Validation}) and no zone the platform runs holds it: publish TXT {RecordName} = {Token} by hand.",
                    domain.HostName, domain.Validation, domain.ValidationRecordName, domain.ValidationToken);
            }

            return;
        }

        await zones.EnsureAsync(zone.Id, new DnsRecordSpec(domain.HostName, "CNAME", edge.TrafficTarget, EdgeOperations.TrafficTtl, PlatformComment), cancellationToken);

        if (domain.Validation == EdgeValidation.Approved)
        {
            return;
        }

        var now = clock.GetUtcNow();

        if (domain.NeedsNewProof(now) && domain.Validation != EdgeValidation.RefreshingValidationToken)
        {
            await edge.RefreshValidationAsync(domain, cancellationToken);
            logger.LogInformation("Front Door wants {HostName} proved again; it was asked for a new token.", domain.HostName);
        }
        else if (domain.HasLiveToken(now))
        {
            await zones.EnsureAsync(
                zone.Id,
                new DnsRecordSpec(domain.ValidationRecordName, "TXT", domain.ValidationToken!, EdgeOperations.ProofTtl, PlatformComment),
                cancellationToken);
        }
    }
}

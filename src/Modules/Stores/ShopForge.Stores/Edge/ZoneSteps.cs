using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Provisioning;
using ShopForge.Stores.Domain;
using ShopForge.Stores.Resolution;

namespace ShopForge.Stores.Edge;

// The row the request saved in the same save as the operation, claimed here. Taken away with it on cancelling.
internal sealed class RegisterZone(DbContext dbContext, TimeProvider clock) : IOperationStep
{
    public string Kind => EdgeOperations.Zone;

    public string Name => "register";

    public bool CanUndo => true;

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await EdgeOperations.StoreZoneAsync(dbContext, EdgeOperations.ZoneNameOf(context.Subject!), cancellationToken) is not { State: StoreZoneState.Waiting } zone
            || (zone.OperationId is { } claimed && claimed != context.OperationId))
        {
            return StepOutcome.Failed("The store has no zone waiting for this operation.");
        }

        zone.Begin(context.OperationId, clock.GetUtcNow());

        return StepOutcome.Done(zone.Id.ToString(), created: true);
    }

    public async Task UndoAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await EdgeOperations.StoreZoneAsync(dbContext, EdgeOperations.ZoneNameOf(context.Subject!), cancellationToken) is { } zone
            && zone.OperationId == context.OperationId)
        {
            dbContext.Remove(zone);
        }
    }
}

// Finds the zone in the platform's account before making it. Cancelled, it deletes a zone it made only while no
// nameserver points at it: one that already answers for the domain would take the merchant's domain off the internet.
internal sealed class MakeZone(DbContext dbContext, IDnsZones zones, TimeProvider clock) : IOperationStep
{
    public string Kind => EdgeOperations.Zone;

    public string Name => "zone";

    public bool CanUndo => true;

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        if (await EdgeOperations.StoreZoneAsync(dbContext, EdgeOperations.ZoneNameOf(context.Subject!), cancellationToken) is not { } storeZone)
        {
            return StepOutcome.Failed("The store's zone is gone.");
        }

        var found = await zones.FindAsync(storeZone.Name, cancellationToken);
        var zone = found ?? await zones.CreateAsync(storeZone.Name, cancellationToken);
        storeZone.Record(zone, clock.GetUtcNow());

        return StepOutcome.Done(zone.Id, created: found is null);
    });

    public async Task UndoAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await zones.ZoneAsync(context.ResourceId!, cancellationToken) is { Status: not DnsZoneStatus.Active })
        {
            await zones.DeleteAsync(context.ResourceId!, cancellationToken);
        }
    }
}

// Waits for the domain's nameservers to point at the zone, which only whoever holds the domain at its registrar can
// change, using up no tries. Cloudflare is asked to look once an hour, as often as it lets a free zone ask.
internal sealed class WaitForDelegation(DbContext dbContext, IDnsZones zones, TimeProvider clock) : IOperationStep
{
    public string Kind => EdgeOperations.Zone;

    public string Name => "delegation";

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        if (await EdgeOperations.StoreZoneAsync(dbContext, EdgeOperations.ZoneNameOf(context.Subject!), cancellationToken) is not { ProviderZoneId: { } zoneId } storeZone)
        {
            return StepOutcome.Failed("The store's zone is gone.");
        }

        if (await zones.ZoneAsync(zoneId, cancellationToken) is not { } zone)
        {
            return StepOutcome.Failed("The zone is gone from Cloudflare.");
        }

        var now = clock.GetUtcNow();

        if (zone.Status == DnsZoneStatus.Active)
        {
            storeZone.Activate(now);

            return StepOutcome.Done();
        }

        var nameServers = string.Join(" and ", zone.NameServers);

        if (EdgeOperations.GaveUp(context, EdgeOperations.DelegationGivesUpAfter, now))
        {
            return StepOutcome.Failed($"The domain's nameservers were not changed in fourteen days. Point them at {nameServers}, then retry.");
        }

        try
        {
            await zones.CheckDelegationAsync(zoneId, cancellationToken);
        }
        catch (ProviderException refused) when (refused.IsThrottled || refused.IsPermanent)
        {
            // A look asked for too soon is refused, which changes nothing: Cloudflare keeps looking by itself.
        }

        return StepOutcome.Waiting($"Point the domain's nameservers at {nameServers}.", EdgeOperations.DelegationCheckEvery);
    });
}

// Proves the store's claims under the zone the way a merchant proves one (D-124): the challenge record, now published by
// the platform in the zone it runs, read back from public DNS. Until every claim is seen, it waits. Binding what it
// proves is the pass's to start, as it is for any proof.
internal sealed class ProveClaims(DbContext dbContext, IDnsZones zones, IDnsTxtRecords dns, StoreResolver resolver, IAuditLog audit, TimeProvider clock)
    : IOperationStep
{
    public string Kind => EdgeOperations.Zone;

    public string Name => "proof";

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        if (await EdgeOperations.StoreZoneAsync(dbContext, EdgeOperations.ZoneNameOf(context.Subject!), cancellationToken) is not { ProviderZoneId: { } zoneId } storeZone)
        {
            return StepOutcome.Failed("The store's zone is gone.");
        }

        var claims = (await dbContext.Set<StoreDomain>().Where(domain => domain.VerifiedAt == null).ToListAsync(cancellationToken))
            .Where(claim => storeZone.Holds(claim.HostName))
            .ToList();
        var now = clock.GetUtcNow();
        var proved = new List<string>();

        foreach (var claim in claims)
        {
            await zones.EnsureAsync(zoneId, EdgeOperations.Proof(claim.ChallengeName, claim.VerificationToken!, context.OperationId), cancellationToken);

            if ((await dns.LookupAsync(claim.ChallengeName, cancellationToken)).Contains(claim.VerificationToken, StringComparer.Ordinal))
            {
                claim.Verify(now);
                audit.Record("domain.verified", claim.HostName);
                proved.Add(claim.HostName);
            }
        }

        // Host lookups are cached, and these have just started resolving.
        await resolver.ForgetAsync(proved, cancellationToken);

        return proved.Count == claims.Count
            ? StepOutcome.Done()
            : StepOutcome.Waiting("Waiting for the domain's nameservers to answer with the platform's records.");
    });
}

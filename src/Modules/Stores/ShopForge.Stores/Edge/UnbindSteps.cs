using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Edge;
using ShopForge.Shared.Provisioning;

namespace ShopForge.Stores.Edge;

// The record that sends traffic first, so nothing reaches the edge for a domain that is on its way off it. A record the
// platform did not write is the merchant's, and stays.
internal sealed class UnbindTraffic(DbContext dbContext, IEdgeDomains edge, IDnsZones zones) : IOperationStep
{
    public string Kind => EdgeOperations.Unbind;

    public string Name => "traffic";

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is { ZoneId: { } zoneId })
        {
            await zones.RemoveAsync(zoneId, context.Subject!, "CNAME", edge.TrafficTarget, cancellationToken);
        }

        return StepOutcome.Done();
    });
}

// Off the route and the security policy, deleting either it was the last domain on.
internal sealed class UnbindRouting(IEdgeDomains edge) : IOperationStep
{
    public string Kind => EdgeOperations.Unbind;

    public string Name => "routing";

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        await edge.LeaveAsync(context.Subject!, cancellationToken);

        return StepOutcome.Done();
    });
}

// Deletes the domain at the edge and waits until it is gone. One the edge is still taking off its route is refused for
// a moment, and asked again.
internal sealed class UnbindEdgeDomain(IEdgeDomains edge) : IOperationStep
{
    private static readonly TimeSpan GoesIn = TimeSpan.FromMinutes(1);

    public string Kind => EdgeOperations.Unbind;

    public string Name => "edge-domain";

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        var hostName = context.Subject!;

        if (await edge.FindAsync(hostName, cancellationToken) is not { } domain)
        {
            return StepOutcome.Done();
        }

        if (domain.Provisioning != EdgeProvisioning.Deleting)
        {
            await edge.DeleteAsync(hostName, cancellationToken);

            if (await edge.FindAsync(hostName, cancellationToken) is null)
            {
                return StepOutcome.Done();
            }
        }

        return StepOutcome.Waiting("Front Door is deleting the domain.", GoesIn);
    });
}

internal sealed class UnbindValidationRecord(DbContext dbContext, IDnsZones zones) : IOperationStep
{
    public string Kind => EdgeOperations.Unbind;

    public string Name => "validation-record";

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is { ZoneId: { } zoneId })
        {
            await zones.RemoveAsync(zoneId, EdgeNames.ValidationRecord(context.Subject!), "TXT", value: null, cancellationToken);
        }

        return StepOutcome.Done();
    });
}

internal sealed class ForgetBinding(DbContext dbContext, IAuditLog audit) : IOperationStep
{
    public string Kind => EdgeOperations.Unbind;

    public string Name => "forget";

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is { } binding)
        {
            dbContext.Remove(binding);
            audit.Record("domain.unbound", binding.HostName);
        }

        return StepOutcome.Done();
    }
}

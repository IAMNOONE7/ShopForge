using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Dns;
using ShopForge.Shared.Edge;
using ShopForge.Shared.Provisioning;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Edge;

// The row the proof saved in the same save as the operation, claimed here. Taken away with it on cancelling.
internal sealed class RegisterBinding(DbContext dbContext, TimeProvider clock) : IOperationStep
{
    public string Kind => EdgeOperations.Bind;

    public string Name => "register";

    public bool CanUndo => true;

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is not { State: DomainBindingState.Binding } binding
            || (binding.OperationId is { } claimed && claimed != context.OperationId))
        {
            return StepOutcome.Failed("The domain has no binding waiting for this operation.");
        }

        binding.Begin(context.OperationId, clock.GetUtcNow());

        return StepOutcome.Done(binding.Id.ToString(), created: true);
    }

    public async Task UndoAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is { } binding && binding.OperationId == context.OperationId)
        {
            dbContext.Remove(binding);
        }
    }
}

// Asks the edge for the domain before making it, and notes whether the platform runs the zone that holds it. A domain
// found was not made here, so cancelling leaves it.
internal sealed class BindEdgeDomain(DbContext dbContext, IEdgeDomains edge, IDnsZones zones, TimeProvider clock) : IOperationStep
{
    public string Kind => EdgeOperations.Bind;

    public string Name => "edge-domain";

    public bool CanUndo => true;

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is not { } binding)
        {
            return StepOutcome.Failed("The domain's binding is gone.");
        }

        var found = await edge.FindAsync(binding.HostName, cancellationToken);
        var domain = found ?? await edge.CreateAsync(binding.HostName, cancellationToken);
        var zone = await zones.HoldingAsync(binding.HostName, cancellationToken);
        var now = clock.GetUtcNow();

        binding.Record(domain, now);
        binding.UseZone(zone?.Id, now);

        return StepOutcome.Done(domain.ResourceName, created: found is null);
    });

    public Task UndoAsync(IStepContext context, CancellationToken cancellationToken) => edge.DeleteAsync(context.Subject!, cancellationToken);
}

// Publishes the edge's token where it reads it, when the platform runs the zone. Otherwise the merchant publishes it,
// and this step makes nothing.
internal sealed class BindValidationRecord(DbContext dbContext, IEdgeDomains edge, IDnsZones zones, TimeProvider clock) : IOperationStep
{
    public string Kind => EdgeOperations.Bind;

    public string Name => "validation-record";

    public bool CanUndo => true;

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is not { } binding)
        {
            return StepOutcome.Failed("The domain's binding is gone.");
        }

        if (binding.ZoneId is not { } zoneId)
        {
            return StepOutcome.Done();
        }

        if (await edge.FindAsync(binding.HostName, cancellationToken) is not { } domain)
        {
            return StepOutcome.Failed("The domain is gone from the edge.");
        }

        var now = clock.GetUtcNow();
        binding.Record(domain, now);

        if (domain.Validation == EdgeValidation.Approved)
        {
            return StepOutcome.Done();
        }

        if (!domain.HasLiveToken(now))
        {
            if (domain.Validation != EdgeValidation.RefreshingValidationToken)
            {
                await edge.RefreshValidationAsync(domain, cancellationToken);
            }

            return StepOutcome.Waiting("Front Door is giving the domain a new validation token.", TimeSpan.FromMinutes(1));
        }

        var record = await zones.EnsureAsync(zoneId, EdgeOperations.Proof(domain.ValidationRecordName, domain.ValidationToken!, context.OperationId), cancellationToken);

        return StepOutcome.Done(record.Record.Id, created: true);
    });

    public async Task UndoAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is { ZoneId: { } zoneId })
        {
            await zones.RemoveAsync(zoneId, EdgeNames.ValidationRecord(context.Subject!), "TXT", value: null, cancellationToken);
        }
    }
}

// Waits for the edge to find its token, using up no tries (D-210). In a zone the platform runs, the record is kept equal
// to the edge's current token, so a renewed one is published the moment it is seen. A token that ran out or was refused
// is replaced first. Otherwise the merchant is told what to publish, and after fourteen days the operation stops for a
// person. Binding and revalidating wait the same way.
internal abstract class ValidationWait(DbContext dbContext, IEdgeDomains edge, IDnsZones zones, TimeProvider clock) : IOperationStep
{
    private static readonly TimeSpan TokenComesIn = TimeSpan.FromMinutes(1);

    public abstract string Kind { get; }

    public string Name => "validation";

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is not { } binding)
        {
            return StepOutcome.Failed("The domain's binding is gone.");
        }

        if (await edge.FindAsync(binding.HostName, cancellationToken) is not { } domain)
        {
            return StepOutcome.Failed("The domain is gone from the edge.");
        }

        var now = clock.GetUtcNow();
        binding.Record(domain, now);

        if (domain.Validation == EdgeValidation.Approved)
        {
            return StepOutcome.Done();
        }

        if (domain.NeedsNewProof(now) && domain.Validation != EdgeValidation.RefreshingValidationToken)
        {
            await edge.RefreshValidationAsync(domain, cancellationToken);

            return StepOutcome.Waiting("Front Door is giving the domain a new validation token.", TokenComesIn);
        }

        if (domain.Validation == EdgeValidation.RefreshingValidationToken || !domain.HasLiveToken(now))
        {
            return StepOutcome.Waiting("Front Door is giving the domain a new validation token.", TokenComesIn);
        }

        var token = domain.ValidationToken!;

        if (binding.ZoneId is { } zoneId)
        {
            await zones.EnsureAsync(zoneId, EdgeOperations.Proof(domain.ValidationRecordName, token, context.OperationId), cancellationToken);
        }

        if (EdgeOperations.GaveUp(context, EdgeOperations.ValidationGivesUpAfter, now))
        {
            return StepOutcome.Failed(binding.RecordsByPlatform
                ? "Front Door has not validated the domain in fourteen days."
                : $"Front Door has not found the TXT record at {domain.ValidationRecordName} in fourteen days. Publish it with the value {token}, then retry.");
        }

        return StepOutcome.Waiting(binding.RecordsByPlatform
            ? "Waiting for Front Door to find the validation record."
            : $"Publish a TXT record at {domain.ValidationRecordName} with the value {token}.");
    });
}

internal sealed class BindValidation(DbContext dbContext, IEdgeDomains edge, IDnsZones zones, TimeProvider clock)
    : ValidationWait(dbContext, edge, zones, clock)
{
    public override string Kind => EdgeOperations.Bind;
}

internal sealed class RevalidateValidation(DbContext dbContext, IEdgeDomains edge, IDnsZones zones, TimeProvider clock)
    : ValidationWait(dbContext, edge, zones, clock)
{
    public override string Kind => EdgeOperations.Revalidate;
}

// Puts the domain on the runtime's route and security policy, the first merchant's domain making both. Cancelling takes
// it off again.
internal sealed class BindRouting(DbContext dbContext, IEdgeDomains edge) : IOperationStep
{
    public string Kind => EdgeOperations.Bind;

    public string Name => "routing";

    public bool CanUndo => true;

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is not { } binding)
        {
            return StepOutcome.Failed("The domain's binding is gone.");
        }

        await edge.JoinAsync(binding.HostName, cancellationToken);

        return StepOutcome.Done(binding.HostName, created: true);
    });

    public Task UndoAsync(IStepContext context, CancellationToken cancellationToken) => edge.LeaveAsync(context.Subject!, cancellationToken);
}

// Waits until the certificate is issued and the domain, its route and its policy are all in place at the edge.
internal sealed class BindCertificate(DbContext dbContext, IEdgeDomains edge, TimeProvider clock) : IOperationStep
{
    public string Kind => EdgeOperations.Bind;

    public string Name => "certificate";

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is not { } binding)
        {
            return StepOutcome.Failed("The domain's binding is gone.");
        }

        if (await edge.FindAsync(binding.HostName, cancellationToken) is not { } domain)
        {
            return StepOutcome.Failed("The domain is gone from the edge.");
        }

        var now = clock.GetUtcNow();
        binding.Record(domain, now);

        if (domain.Provisioning == EdgeProvisioning.Failed || domain.Deployment == EdgeDeployment.Failed)
        {
            return StepOutcome.Failed("Front Door could not put the domain in place.");
        }

        var membership = await edge.MembershipAsync(binding.HostName, cancellationToken);

        if (domain.IsReady && membership.Settled)
        {
            return StepOutcome.Done();
        }

        return EdgeOperations.GaveUp(context, EdgeOperations.CertificateGivesUpAfter, now)
            ? StepOutcome.Failed("Front Door has not issued the domain's certificate in a day.")
            : StepOutcome.Waiting("Front Door is issuing the domain's certificate.");
    });
}

// Points the domain at the edge, in a zone the platform runs. Last, so that a domain already serving a site somewhere
// else moves only once it can be served here. Otherwise the merchant points it, and this step makes nothing.
internal sealed class BindTraffic(DbContext dbContext, IEdgeDomains edge, IDnsZones zones) : IOperationStep
{
    public string Kind => EdgeOperations.Bind;

    public string Name => "traffic";

    public bool CanUndo => true;

    public Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) => EdgeOperations.GuardAsync(async () =>
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is not { } binding)
        {
            return StepOutcome.Failed("The domain's binding is gone.");
        }

        if (binding.ZoneId is not { } zoneId)
        {
            return StepOutcome.Done();
        }

        var record = await zones.EnsureAsync(zoneId, EdgeOperations.Traffic(binding.HostName, edge.TrafficTarget, context.OperationId), cancellationToken);

        return StepOutcome.Done(record.Record.Id, created: true);
    });

    public async Task UndoAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is { ZoneId: { } zoneId })
        {
            await zones.RemoveAsync(zoneId, context.Subject!, "CNAME", edge.TrafficTarget, cancellationToken);
        }
    }
}

internal sealed class ActivateBinding(DbContext dbContext, IAuditLog audit, TimeProvider clock) : IOperationStep
{
    public string Kind => EdgeOperations.Bind;

    public string Name => "activate";

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await EdgeOperations.BindingAsync(dbContext, context.Subject, cancellationToken) is not { } binding)
        {
            return StepOutcome.Failed("The domain's binding is gone.");
        }

        binding.Bind(clock.GetUtcNow());
        audit.Record("domain.bound", binding.HostName);

        return StepOutcome.Done();
    }
}

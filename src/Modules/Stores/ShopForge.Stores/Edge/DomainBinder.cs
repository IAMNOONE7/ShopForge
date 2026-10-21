using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Edge;
using ShopForge.Shared.Provisioning;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Edge;

// Starts putting a proved domain on the edge, or taking one off, wherever that is asked for: a proof, a removal, and
// the pass that catches up with whatever could not start at the time (D-212). Nothing waits for the edge here.
internal sealed class DomainBinder(DbContext dbContext, IStoreContext storeContext, IEdgeDomains edge, IProvisioning provisioning, TimeProvider clock)
{
    // The binding's operation, or null when the edge is not this deployment's, the domain is bound already, or another
    // operation still has the host; the pass tries again then.
    public async Task<Guid?> StartBindingAsync(string hostName, CancellationToken cancellationToken)
    {
        // A host belongs to one binding across the platform, so the check lifts the store's filter.
        if (!edge.IsConfigured
            || await dbContext.Set<DomainBinding>().IgnoreQueryFilters().AnyAsync(binding => binding.HostName == hostName, cancellationToken))
        {
            return null;
        }

        var binding = new DomainBinding(
            storeContext.TenantId ?? throw new InvalidOperationException("A domain is bound inside a company."),
            storeContext.StoreId ?? throw new InvalidOperationException("A domain is bound inside a store."),
            hostName,
            clock.GetUtcNow());

        // Saved with the operation, in its one save, so the worker never finds an operation without its row. The row then
        // names its operation for the admin; a worker that claimed it in between wrote the same.
        dbContext.Add(binding);
        var started = await provisioning.StartAsync(EdgeOperations.Bind, hostName, input: null, cancellationToken);

        if (!started.Started)
        {
            dbContext.Entry(binding).State = EntityState.Detached;

            return null;
        }

        binding.Begin(started.OperationId, clock.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        return started.OperationId;
    }

    // The unbinding's operation, or null when there is nothing to take off or something is still working on the host. The
    // store's own binding or revalidation still under way is asked to stop instead, which takes away what it made, and
    // an unbinding under way is left to finish; the pass starts the unbinding once the host is free.
    public async Task<Guid?> StartUnbindingAsync(string hostName, CancellationToken cancellationToken)
    {
        if (await EdgeOperations.BindingAsync(dbContext, hostName, cancellationToken) is not { } binding)
        {
            return null;
        }

        binding.Unbind(clock.GetUtcNow());

        if (await provisioning.UnfinishedAsync(hostName, cancellationToken) is { } running)
        {
            if (running.Kind != EdgeOperations.Unbind)
            {
                await provisioning.CancelAsync(running.OperationId, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return null;
        }

        var started = await provisioning.StartAsync(EdgeOperations.Unbind, hostName, input: null, cancellationToken);

        if (started.Started)
        {
            binding.Begin(started.OperationId, clock.GetUtcNow());
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return started.Started ? started.OperationId : null;
    }
}

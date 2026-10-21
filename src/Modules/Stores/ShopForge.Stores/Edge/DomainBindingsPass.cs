using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Edge;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Edge;

// Keeps every store's proved domains and the edge in step (D-212): a proved domain with no binding is bound, and a
// binding whose domain the store has let go of is taken off. What a proof or a removal could not start at the time,
// because something was still working on the host, starts here, and so does the binding of what a zone proved. Every
// quarter of an hour, by whichever replica takes it, and only for the stores that have something to do.
internal sealed class DomainBindingsPass(IServiceProvider services, ILogger<DomainBindingsPass> logger) : IScheduledPass
{
    public string Name => "domain-bindings";

    public TimeSpan Period => TimeSpan.FromMinutes(15);

    Task IScheduledPass.RunAsync(CancellationToken cancellationToken) => RunAsync(cancellationToken);

    // How many operations it started.
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<StoreReference> stores;

        await using (var scope = services.CreateAsyncScope())
        {
            if (!scope.ServiceProvider.GetRequiredService<IEdgeDomains>().IsConfigured)
            {
                return 0;
            }

            stores = await StoresWithWorkAsync(scope.ServiceProvider, cancellationToken);
        }

        var started = 0;

        foreach (var store in stores)
        {
            try
            {
                started += await RunForStoreAsync(store, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Keeping store {StoreId}'s domains at the edge failed; the next pass tries again.", store.StoreId);
            }
        }

        return started;
    }

    // One store's, which is also how a test runs it: the suite's stores share one database, and another test's proved
    // domains are not this one's to bind.
    public async Task<int> RunForStoreAsync(StoreReference store, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(store.StoreId, store.TenantId);
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var binder = scope.ServiceProvider.GetRequiredService<DomainBinder>();

        var proved = await dbContext.Set<StoreDomain>()
            .Where(domain => domain.VerifiedAt != null && domain.VerificationToken != null)
            .Select(domain => domain.HostName)
            .ToListAsync(cancellationToken);
        var bindings = await dbContext.Set<DomainBinding>().AsNoTracking().ToListAsync(cancellationToken);
        var started = 0;

        foreach (var hostName in proved.Where(hostName => bindings.All(binding => binding.HostName != hostName)))
        {
            started += await binder.StartBindingAsync(hostName, cancellationToken) is null ? 0 : 1;
        }

        foreach (var binding in bindings.Where(binding => binding.State == DomainBindingState.Unbinding || !proved.Contains(binding.HostName)))
        {
            started += await binder.StartUnbindingAsync(binding.HostName, cancellationToken) is null ? 0 : 1;
        }

        return started;
    }

    // A store's domain the merchant proved (a platform subdomain needs nothing: the template's wildcard serves it) with
    // no binding anywhere, or a binding whose domain is gone or on its way off the edge.
    internal static async Task<IReadOnlyList<StoreReference>> StoresWithWorkAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var dbContext = provider.GetRequiredService<DbContext>();
        var domains = dbContext.Set<StoreDomain>().IgnoreQueryFilters();
        var bindings = dbContext.Set<DomainBinding>().IgnoreQueryFilters();

        var unbound = domains
            .Where(domain => domain.VerifiedAt != null && domain.VerificationToken != null && !bindings.Any(binding => binding.HostName == domain.HostName))
            .Select(domain => domain.StoreId);
        var orphaned = bindings
            .Where(binding => binding.State == DomainBindingState.Unbinding
                || !domains.Any(domain => domain.HostName == binding.HostName && domain.StoreId == binding.StoreId && domain.VerifiedAt != null))
            .Select(binding => binding.StoreId);
        var storeIds = await unbound.Union(orphaned).ToListAsync(cancellationToken);
        var directory = provider.GetRequiredService<IStoreDirectory>();
        var stores = new List<StoreReference>();

        foreach (var storeId in storeIds)
        {
            if (await directory.FindAsync(storeId, cancellationToken) is { } store)
            {
                stores.Add(store);
            }
        }

        return stores;
    }
}

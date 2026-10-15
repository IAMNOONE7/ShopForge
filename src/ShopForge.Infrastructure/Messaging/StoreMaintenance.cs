using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Messaging;

// The nightly housekeeping: every module's cleanup in every store, then what no store owns. Once a day, by whichever
// replica takes it, at a time the database keeps — so a deployment that restarts every few hours still has it done
// (D-202). One store's cleanup failing is logged and the rest carries on, as the catch-up pass already did.
internal sealed class StoreMaintenance(IServiceProvider services, ILogger<StoreMaintenance> logger) : IScheduledPass
{
    public string Name => "store-maintenance";

    public TimeSpan Period => TimeSpan.FromHours(24);

    Task IScheduledPass.RunAsync(CancellationToken cancellationToken) => RunAsync(cancellationToken);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var stores = await scope.ServiceProvider.GetRequiredService<IStoreDirectory>().AllAsync(cancellationToken);
        var removed = 0;

        foreach (var store in stores)
        {
            removed += await RunForStoreAsync(store, cancellationToken);
        }

        removed += await RunOutsideStoresAsync(cancellationToken);
        logger.LogInformation("Maintenance removed {Count} rows in total.", removed);

        return removed;
    }

    // Rows no store owns are swept once, with nothing in scope, rather than once per store (D-118).
    private async Task<int> RunOutsideStoresAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var removed = 0;

        foreach (var maintenance in scope.ServiceProvider.GetServices<IMaintenanceOutsideStores>())
        {
            removed += await RunOneAsync(maintenance.Name, null, () => maintenance.RunAsync(cancellationToken), cancellationToken);
        }

        return removed;
    }

    private async Task<int> RunForStoreAsync(StoreReference store, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(store.StoreId, store.TenantId);

        var removed = 0;

        foreach (var maintenance in scope.ServiceProvider.GetServices<IStoreMaintenance>())
        {
            removed += await RunOneAsync(maintenance.Name, store.StoreId, () => maintenance.RunAsync(cancellationToken), cancellationToken);
        }

        return removed;
    }

    private async Task<int> RunOneAsync(string name, Guid? storeId, Func<Task<int>> run, CancellationToken cancellationToken)
    {
        try
        {
            var count = await run();

            if (count > 0)
            {
                logger.LogInformation("{Maintenance} removed {Count} rows in store {StoreId}.", name, count, storeId);
            }

            return count;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "{Maintenance} failed in store {StoreId}; tomorrow's pass tries again.", name, storeId);

            return 0;
        }
    }
}

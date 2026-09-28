using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Messaging;

internal sealed class StoreMaintenance(IServiceProvider services, ILogger<StoreMaintenance> logger)
{
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
            var count = await maintenance.RunAsync(cancellationToken);

            if (count > 0)
            {
                logger.LogInformation("{Maintenance} removed {Count} rows outside any store.", maintenance.Name, count);
            }

            removed += count;
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
            var count = await maintenance.RunAsync(cancellationToken);

            if (count > 0)
            {
                logger.LogInformation("{Maintenance} removed {Count} rows in store {StoreId}.", maintenance.Name, count, store.StoreId);
            }

            removed += count;
        }

        return removed;
    }
}

internal sealed class MaintenanceWorker(StoreMaintenance maintenance, TimeProvider clock, ILogger<MaintenanceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period, clock);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await maintenance.RunAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Store maintenance failed; the next run picks it up.");
            }
        }
    }
}

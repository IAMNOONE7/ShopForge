using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Messaging;

// The short-period pass: work that has to happen again soon because somebody is waiting for it, as against the
// nightly housekeeping that deletes rows nobody will come back for (D-176). Short enough that a shopper whose
// notification was lost is not left wondering, long enough that a gateway is not asked about the same transaction
// constantly — and asked by one replica, not by every one of them (D-202).
internal sealed class StoreCatchUp(IServiceProvider services, ILogger<StoreCatchUp> logger) : IScheduledPass
{
    public string Name => "store-catch-up";

    public TimeSpan Period => TimeSpan.FromMinutes(5);

    Task IScheduledPass.RunAsync(CancellationToken cancellationToken) => RunAsync(cancellationToken);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var stores = await scope.ServiceProvider.GetRequiredService<IStoreDirectory>().AllAsync(cancellationToken);
        var settled = 0;

        foreach (var store in stores)
        {
            settled += await RunForStoreAsync(store, cancellationToken);
        }

        return settled;
    }

    private async Task<int> RunForStoreAsync(StoreReference store, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<StoreContext>().Set(store.StoreId, store.TenantId);

        var settled = 0;

        foreach (var catchUp in scope.ServiceProvider.GetServices<IStoreCatchUp>())
        {
            try
            {
                var count = await catchUp.RunAsync(cancellationToken);

                if (count > 0)
                {
                    logger.LogInformation("{CatchUp} settled {Count} in store {StoreId}.", catchUp.Name, count, store.StoreId);
                }

                settled += count;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One shop whose gateway is unreachable must not stop every other shop's payments finishing.
                logger.LogError(exception, "{CatchUp} failed in store {StoreId}; the next pass picks it up.", catchUp.Name, store.StoreId);
            }
        }

        return settled;
    }
}

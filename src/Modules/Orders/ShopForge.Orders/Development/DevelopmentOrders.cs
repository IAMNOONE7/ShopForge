using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Publishing;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Development;

public static class DevelopmentOrders
{
    public static async Task SeedDevelopmentMethodsAsync(
        this IServiceProvider services,
        Guid tenantId,
        IEnumerable<Guid> storeIds,
        CancellationToken cancellationToken = default)
    {
        foreach (var storeId in storeIds)
        {
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<StoreContext>().Set(storeId, tenantId);
            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

            if (await dbContext.Set<PaymentMethod>().AnyAsync(cancellationToken))
            {
                continue;
            }

            var settings = await scope.ServiceProvider.GetRequiredService<ICurrentStoreSettings>().GetAsync(cancellationToken);

            await scope.ServiceProvider.GetRequiredService<IStoreInitializer>().InitializeAsync(new NewStore(settings.Currency), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

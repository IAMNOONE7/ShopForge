using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Publishing;
using ShopForge.Orders.Shipping;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Development;

public static class DevelopmentOrders
{
    // Methods priced by the parcel on the placeholder prices, switched off: the merchant's preview has something to show
    // locally, and nothing invented reaches a shopper (D-219). A store in another currency has no placeholder prices.
    private static readonly (string Code, string Name, string Service, bool ToAPoint)[] ParcelMethods =
    [
        ("z-box-by-the-parcel", "Z-BOX by the parcel", "packeta-cz-zbox", true),
        ("doorstep-by-the-parcel", "Doorstep by the parcel", "packeta-cz-home", false),
    ];

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

            var settings = await scope.ServiceProvider.GetRequiredService<ICurrentStoreSettings>().GetAsync(cancellationToken);

            if (!await dbContext.Set<PaymentMethod>().AnyAsync(cancellationToken))
            {
                await scope.ServiceProvider.GetRequiredService<IStoreInitializer>().InitializeAsync(new NewStore(settings.Currency), cancellationToken);
            }

            foreach (var (code, name, service, toAPoint) in ParcelMethods)
            {
                if (await dbContext.Set<ShippingMethod>().AnyAsync(method => method.Code == code, cancellationToken))
                {
                    continue;
                }

                var method = new ShippingMethod(storeId, code, name, "packeta", price: 0m, vatRate: 21m, settings.Currency, toAPoint);
                method.Update(name, 0m, 21m, isActive: false, toAPoint, settings.Currency);
                method.SendBy(ShippingPricing.PerParcel, service, parcelMaxWeightGrams: null, parcelLargest: null);
                PlaceholderRates.StartFrom(method, settings.Currency);
                dbContext.Add(method);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

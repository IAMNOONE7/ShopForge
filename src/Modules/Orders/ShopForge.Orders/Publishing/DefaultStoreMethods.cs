using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Payments;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Publishing;

// A new store starts with methods it can actually use; the admin renames, prices or deactivates them.
internal sealed class DefaultStoreMethods(DbContext dbContext, IStoreContext storeContext) : IStoreInitializer
{
    public Task InitializeAsync(NewStore store, CancellationToken cancellationToken)
    {
        var storeId = storeContext.StoreId!.Value;

        dbContext.Add(new PaymentMethod(storeId, "bank-transfer", "Bank transfer", ManualPaymentProvider.ProviderKey));
        dbContext.Add(new ShippingMethod(storeId, "personal-pickup", "Personal pickup", ManualPaymentProvider.ProviderKey, price: 0m, vatRate: 21m, store.Currency));

        return Task.CompletedTask;
    }
}

using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Payments;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Orders.Publishing;

// A new store starts with the payment method that needs no account, which the admin renames or deactivates. It starts
// with no delivery method. Delivery is Packeta or the store's own, at prices the merchant sets; a free personal pickup
// nobody offers made "delivery from" say delivery costs nothing. Readiness refuses to publish a store without one (D-223).
internal sealed class DefaultStoreMethods(DbContext dbContext, IStoreContext storeContext) : IStoreInitializer
{
    public Task InitializeAsync(NewStore store, CancellationToken cancellationToken)
    {
        dbContext.Add(new PaymentMethod(storeContext.StoreId!.Value, "bank-transfer", "Bank transfer", ManualPaymentProvider.ProviderKey));

        return Task.CompletedTask;
    }
}

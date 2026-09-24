using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Provisioning;

internal sealed class CurrentStoreSettings(DbContext dbContext, IStoreContext storeContext) : ICurrentStoreSettings
{
    public async Task<StoreSettings> GetAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<Store>()
            .Where(store => store.Id == storeContext.StoreId)
            .Select(store => new StoreSettings(
                store.Name,
                store.Currency,
                store.Culture,
                store.ReturnWindowDays,
                store.Company == null
                    ? null
                    : new SellerDetails(
                        store.Company.LegalName,
                        store.Company.Line1,
                        store.Company.City,
                        store.Company.PostalCode,
                        store.Company.Country,
                        store.Company.RegistrationNumber,
                        store.Company.VatNumber)))
            .SingleAsync(cancellationToken);
}

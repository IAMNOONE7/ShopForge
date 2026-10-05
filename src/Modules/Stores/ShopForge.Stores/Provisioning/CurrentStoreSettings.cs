using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Provisioning;

internal sealed class CurrentStoreSettings(DbContext dbContext, IStoreContext storeContext, IStoreUrls urls) : ICurrentStoreSettings
{
    public async Task<StoreSettings> GetAsync(CancellationToken cancellationToken)
    {
        // Mail is read away from the site, so the logo needs somewhere real to be fetched from. A store with
        // no proved domain has nowhere, and no logo in its mail is better than a broken image (D-121, D-164).
        var address = await urls.FindAsync(cancellationToken);

        return await dbContext.Set<Store>()
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
                        store.Company.VatNumber),
                new StoreBranding(
                    store.Theme.PrimaryColor,
                    store.LogoPath == null ? null : address == null ? null : address.Logo)))
            .SingleAsync(cancellationToken);
    }
}

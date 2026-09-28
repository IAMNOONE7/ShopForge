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
                        store.Company.VatNumber),
                new StoreBranding(
                    store.Theme.PrimaryColor,

                    // Mail is read away from the site, so the logo needs somewhere real to be fetched from: the
                    // store's own primary host (D-121).
                    store.LogoPath == null
                        ? null
                        : "https://" + store.Domains.Where(domain => domain.IsPrimary).Select(domain => domain.HostName).FirstOrDefault()
                            + "/api/storefront/store/logo")))
            .SingleAsync(cancellationToken);
}

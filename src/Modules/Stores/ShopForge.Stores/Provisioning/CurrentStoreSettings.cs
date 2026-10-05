using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Provisioning;

internal sealed class CurrentStoreSettings(DbContext dbContext, IStoreContext storeContext, IStoreUrls urls) : ICurrentStoreSettings
{
    public async Task<StoreSettings> GetAsync(CancellationToken cancellationToken)
    {
        var found = await dbContext.Set<Store>()
            .Where(store => store.Id == storeContext.StoreId)
            .Select(store => new
            {
                store.LogoPath,
                Settings = new StoreSettings(
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
                new StoreBranding(store.Theme.PrimaryColor, LogoUrl: null),
                new StoreSeo(store.SeoTitleSuffix, store.SeoDescription, store.SeoSocialImageUrl, store.SeoNoIndex)),
            })
            .SingleAsync(cancellationToken);

        if (found.LogoPath is null)
        {
            return found.Settings;
        }

        // Mail is read away from the site, so the logo needs somewhere real to be fetched from. A store with
        // no proved domain has nowhere, and no logo in its mail is better than a broken image (D-121, D-164).
        // The address is only looked up for a store that has a logo to put at it.
        var address = await urls.FindAsync(cancellationToken);

        return found.Settings with { Branding = found.Settings.Branding with { LogoUrl = address?.Logo } };
    }
}

using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Stores;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Provisioning;

internal sealed class CurrentStoreSettings(DbContext dbContext, IStoreContext storeContext) : ICurrentStoreSettings
{
    public async Task<StoreSettings> GetAsync(CancellationToken cancellationToken)
    {
        var found = await dbContext.Set<Store>()
            .Where(store => store.Id == storeContext.StoreId)
            .Select(store => new
            {
                store.LogoPath,

                // The store's own address: the primary domain, and only once it is proved. A domain can only
                // be made primary after it is proved, so this asks for both rather than trusting that rule to
                // stay true somewhere else. `IStoreUrls` reads it from here, so it is stated once.
                Host = store.Domains
                    .Where(domain => domain.IsPrimary && domain.VerifiedAt != null)
                    .Select(domain => domain.HostName)
                    .FirstOrDefault(),
                store.Culture,
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

        var address = found.Host is null ? null : new StoreAddress(found.Host, Language(found.Culture));

        // Mail is read away from the site, so the logo needs somewhere real to be fetched from. A store with
        // no proved domain has nowhere, and no logo in its mail is better than a broken image (D-121, D-164).
        return found.Settings with
        {
            Branding = found.Settings.Branding with { LogoUrl = found.LogoPath is null ? null : address?.Logo },
            Address = address,
        };
    }

    // A culture names a language and a place; a page is written in the language. "cs-CZ" is Czech wherever it
    // is read.
    private static string Language(string culture) => culture.Split('-')[0].ToLowerInvariant();
}

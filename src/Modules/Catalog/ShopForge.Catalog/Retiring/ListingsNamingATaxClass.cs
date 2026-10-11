using Microsoft.EntityFrameworkCore;
using ShopForge.Catalog.Domain;
using ShopForge.Shared.Tax;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Catalog.Retiring;

// The catalogue's answer when a shop wants a tax class gone: the listings pointed at it, with the count,
// because "it is in use" tells a merchant nothing about what to do next (D-180, D-187).
internal sealed class ListingsNamingATaxClass(DbContext dbContext) : ITaxClassUsers
{
    public async Task<string?> WhatNamesAsync(string taxClass, CancellationToken cancellationToken)
    {
        var listings = await dbContext.Set<StoreProduct>()
            .IgnoreQueryFilters([TenancyFilters.Archived])
            .CountAsync(listing => listing.TaxClass == taxClass, cancellationToken);

        return listings == 0 ? null : $"{listings} {(listings == 1 ? "listing is" : "listings are")} taxed as it";
    }
}

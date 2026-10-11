using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Tax;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Tax;

internal sealed class StoreTaxRates(DbContext dbContext) : ITaxRates
{
    public async Task<IReadOnlyList<string>> ClassesAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<TaxClass>()
            .AsNoTracking()
            .OrderBy(taxClass => taxClass.Code)
            .Select(taxClass => taxClass.Code)
            .ToListAsync(cancellationToken);

    public async Task<DestinationRates> ForAsync(string? destinationCountry, string? homeCountry, CancellationToken cancellationToken)
    {
        var destination = Normalize(destinationCountry);
        var home = Normalize(homeCountry);

        if (destination is null && home is null)
        {
            return DestinationRates.Nowhere;
        }

        // Both countries in one read, because the destination's rate is the one that applies and the shop's
        // own is what applies where the shop has said nothing about the destination.
        var rates = await dbContext.Set<TaxRate>()
            .AsNoTracking()
            .Where(rate => rate.Country == destination || rate.Country == home)
            .Select(rate => new { rate.TaxClass, rate.Country, rate.Rate })
            .ToListAsync(cancellationToken);

        var byClass = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var rate in rates.Where(rate => rate.Country == home))
        {
            byClass[rate.TaxClass] = rate.Rate;
        }

        foreach (var rate in rates.Where(rate => rate.Country == destination))
        {
            byClass[rate.TaxClass] = rate.Rate;
        }

        return new DestinationRates(destination ?? home, byClass);
    }

    private static string? Normalize(string? country) =>
        Countries.IsValid(country) ? country!.ToUpperInvariant() : null;
}

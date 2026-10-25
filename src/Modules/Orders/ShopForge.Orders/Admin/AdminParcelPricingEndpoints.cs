using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Orders.Shipping;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Catalog;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Stores;

namespace ShopForge.Orders.Admin;

// A method priced by the parcel has a rate card, and a merchant can ask what any cart would cost by any method without
// a shopper (D-219). The preview is the quote the checkout will ask, not a copy of it.
internal static class AdminParcelPricingEndpoints
{
    // More bands than this is a price list nobody could read at the checkout either.
    private const int MostBands = 50;

    // A preview is a cart, and a cart this long is a script rather than a screen.
    private const int MostLines = 200;

    // What one cart line holds (D-145): ninety-nine of a thing.
    private const int MostQuantity = 99;

    public static void MapAdminParcelPricing(this IEndpointRouteBuilder storeAdmin)
    {
        storeAdmin.MapPut("/shipping-methods/{code}/rates", SetRatesAsync).RequireAuthorization(AdminPolicies.StoreManagement);
        storeAdmin.MapPost("/shipping-methods/{code}/quote", QuoteAsync);
    }

    // The whole card at once: bands are prices relative to each other, and half a new price list beside half an old one
    // is neither. A change is audited with its before and after, and affects new quotes only.
    private static async Task<Results<Ok<AdminShippingMethodResponse>, ValidationProblem, NotFound>> SetRatesAsync(
        string code,
        RateCardRequest request,
        DbContext dbContext,
        ICurrentStoreSettings storeSettings,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var method = await dbContext.Set<ShippingMethod>().SingleOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);

        if (method is null)
        {
            return TypedResults.NotFound();
        }

        var currency = (await storeSettings.GetAsync(cancellationToken)).Currency;
        var bands = request.Bands ?? [];
        var errors = new RequestErrors()
            .Check(method.Pricing == ShippingPricing.PerParcel, "pricing", "Only a method priced by the parcel has a rate card.")
            .Check(request.Source is not null, "source", "Say where the prices come from: carrier, own or placeholder.")
            .Check(request.Source != RateSource.Carrier || request.PricedOn is not null, "pricedOn", "Say the date of the carrier's price list.")
            .Check(bands.Count is >= 1 and <= MostBands, "bands", $"A rate card has from 1 to {MostBands} bands.")
            .Check(bands.All(band => band.UpToGrams > 0), "bands", "Each band ends at a weight of more than nothing.")
            .Check(bands.Zip(bands.Skip(1)).All(pair => pair.Second.UpToGrams > pair.First.UpToGrams), "bands", "Each band ends at a heavier weight than the one before it.")
            .Check(bands.All(band => band.Price >= 0 && currency.Holds(band.Price)), "bands", $"A price is zero or more, with at most {currency.Decimals} decimals.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var before = Card(method);
        method.PriceParcels(request.Source!.Value, request.PricedOn, [.. bands.Select(band => new RateBand(band.UpToGrams, band.Price))], currency);
        audit.Record("shipping-method.rates-changed", method.Name, new { Before = before, After = Card(method) });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(AdminMethodEndpoints.ShippingResponse(method));
    }

    // Read-only, so a configuration can be tried before any shopper meets it, switched on or not.
    private static async Task<Results<Ok<ShippingQuote>, ValidationProblem, NotFound>> QuoteAsync(
        string code,
        QuoteRequest request,
        DbContext dbContext,
        ISellableProducts products,
        ShippingQuotes quotes,
        CancellationToken cancellationToken)
    {
        var method = await dbContext.Set<ShippingMethod>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Code == code, cancellationToken);

        if (method is null)
        {
            return TypedResults.NotFound();
        }

        var lines = request.Lines ?? [];
        var errors = new RequestErrors()
            .Check(lines.Count is >= 1 and <= MostLines, "lines", $"A preview takes from 1 to {MostLines} lines.")
            .Check(lines.All(line => line.Quantity is >= 1 and <= MostQuantity), "lines", $"A line holds from 1 to {MostQuantity} of a thing.")
            .Check(request.Country is null || (request.Country.Trim().Length == 2 && request.Country.Trim().All(char.IsAsciiLetter)), "country", "A country is its two-letter code, such as CZ.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var listings = await products.FindAsync([.. lines.Select(line => line.StoreProductId).Distinct()], cancellationToken);
        var items = new List<QuotedItem>();

        foreach (var (line, number) in lines.Select((line, index) => (line, index + 1)))
        {
            if (listings.SingleOrDefault(listing => listing.StoreProductId == line.StoreProductId)?.Form(line.VariantId) is not { } variant)
            {
                return new RequestErrors().Check(false, "lines", $"Line {number} is not a form of a product on sale in this store.").ToProblem();
            }

            items.Add(new QuotedItem(variant, line.Quantity));
        }

        return TypedResults.Ok(await quotes.QuoteAsync(method, items, request.Country, cancellationToken));
    }

    private static object Card(ShippingMethod method) =>
        new
        {
            Source = method.RatesFrom?.ToString(),
            method.RatesPricedOn,
            Bands = method.RateBands.Select(band => new[] { band.UpToGrams, band.Price }),
        };
}

internal sealed record RateCardRequest(RateSource? Source, DateOnly? PricedOn, IReadOnlyList<RateBandRequest>? Bands);

internal sealed record RateBandRequest(int UpToGrams, decimal Price);

internal sealed record QuoteRequest(string? Country, IReadOnlyList<QuoteLineRequest>? Lines);

internal sealed record QuoteLineRequest(Guid StoreProductId, Guid? VariantId, int Quantity);
